"use strict";

const fs = require("fs");
const http = require("http");
const https = require("https");
const path = require("path");
const ftp = require("basic-ftp");

const helperDirectory = __dirname;
const repositoryRoot = path.resolve(helperDirectory, "..", "..");
const sourceDirectory = path.join(repositoryRoot, "local-deployment", "safespeak-web");
const envPath = path.join(helperDirectory, ".env");

// Nothing outside this list can be uploaded. In particular, the local upload
// README, ZIP archive, credentials, source files, and repository files are not
// eligible for transfer.
const publicFiles = [
    "assets/site.css",
    "assets/safespeak-icon.png",
    "privacy/index.html",
    "support/index.html",
    "accessibility/index.html",
    "license/index.html",
    "robots.txt",
    "index.html"
];

const publicRoutes = ["/", "/privacy/", "/support/", "/accessibility/", "/license/"];

function loadEnv(filePath) {
    if (!fs.existsSync(filePath)) return;

    const content = fs.readFileSync(filePath, "utf8");
    for (const rawLine of content.split(/\r?\n/)) {
        const line = rawLine.trim();
        if (!line || line.startsWith("#")) continue;

        const equals = line.indexOf("=");
        if (equals < 1) continue;

        const key = line.slice(0, equals).trim();
        let value = line.slice(equals + 1).trim();
        if ((value.startsWith('"') && value.endsWith('"')) ||
            (value.startsWith("'") && value.endsWith("'"))) {
            value = value.slice(1, -1);
        }

        if (process.env[key] === undefined) process.env[key] = value;
    }
}

function required(name) {
    const value = process.env[name];
    if (!value || !value.trim()) throw new Error(`Missing required setting: ${name}`);
    return value.trim();
}

function asBoolean(name, fallback) {
    const value = process.env[name];
    if (value === undefined || value === "") return fallback;
    if (value.toLowerCase() === "true") return true;
    if (value.toLowerCase() === "false") return false;
    throw new Error(`${name} must be true or false.`);
}

function validateRemoteDirectory(remoteDirectory) {
    const unixPath = remoteDirectory.replace(/\\/g, "/");
    if (unixPath.split("/").includes("..")) {
        throw new Error("FTP_REMOTE_DIR must not contain '..' path segments.");
    }
    const normalized = path.posix.normalize(unixPath);
    if (!normalized.startsWith("/") || normalized === "/") {
        throw new Error("FTP_REMOTE_DIR must be a specific absolute directory without '..'.");
    }
    if (!normalized.endsWith("/public_html")) {
        throw new Error("FTP_REMOTE_DIR must end with /public_html for this deployment helper.");
    }
    return normalized;
}

function localFiles() {
    if (!fs.existsSync(sourceDirectory)) {
        throw new Error(`Website source folder not found: ${sourceDirectory}`);
    }

    return publicFiles.map(relativePath => {
        const localPath = path.resolve(sourceDirectory, ...relativePath.split("/"));
        const relativeCheck = path.relative(sourceDirectory, localPath);
        if (relativeCheck.startsWith("..") || path.isAbsolute(relativeCheck)) {
            throw new Error(`Upload target escapes the website folder: ${relativePath}`);
        }

        const stat = fs.lstatSync(localPath);
        if (!stat.isFile() || stat.isSymbolicLink()) {
            throw new Error(`Upload target must be a regular file: ${relativePath}`);
        }

        return { relativePath, localPath, bytes: stat.size };
    });
}

function formatBytes(bytes) {
    if (bytes >= 1024 * 1024) return `${(bytes / (1024 * 1024)).toFixed(2)} MB`;
    if (bytes >= 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${bytes} bytes`;
}

function printPlan(files, mode) {
    const totalBytes = files.reduce((total, file) => total + file.bytes, 0);
    console.log("");
    console.log("SafeSpeak website deployment");
    console.log(`Mode: ${mode}`);
    console.log(`Files: ${files.length} (${formatBytes(totalBytes)})`);
    for (const file of files) {
        console.log(`  ${file.relativePath.padEnd(30)} ${formatBytes(file.bytes)}`);
    }
    console.log("");
}

function readCertificateAuthority() {
    const configured = process.env.FTP_CA_FILE;
    if (!configured) return undefined;

    const caPath = path.resolve(helperDirectory, configured);
    const relativeCheck = path.relative(helperDirectory, caPath);
    if (relativeCheck.startsWith("..") || path.isAbsolute(relativeCheck)) {
        throw new Error("FTP_CA_FILE must be stored inside tools/website-deploy.");
    }
    if (!fs.existsSync(caPath)) throw new Error(`FTP_CA_FILE not found: ${configured}`);
    return fs.readFileSync(caPath);
}

function transferConfiguration() {
    const secure = asBoolean("FTP_SECURE", true);
    const rejectUnauthorized = asBoolean("FTP_REJECT_UNAUTHORIZED", true);
    if (!secure) throw new Error("Plain FTP is disabled. FTP_SECURE must be true.");
    if (!rejectUnauthorized) {
        throw new Error("Unverified TLS is disabled. FTP_REJECT_UNAUTHORIZED must be true.");
    }

    const port = Number.parseInt(process.env.FTP_PORT || "21", 10);
    if (!Number.isInteger(port) || port < 1 || port > 65535) {
        throw new Error("FTP_PORT must be a valid TCP port.");
    }

    const host = required("FTP_HOST");
    return {
        host,
        port,
        user: required("FTP_USER"),
        password: required("FTP_PASS"),
        remoteDirectory: validateRemoteDirectory(required("FTP_REMOTE_DIR")),
        secureOptions: {
            rejectUnauthorized: true,
            servername: host,
            ca: readCertificateAuthority()
        }
    };
}

async function connect(configuration) {
    const client = new ftp.Client(20_000);
    client.ftp.verbose = false;
    try {
        await client.access({
            host: configuration.host,
            port: configuration.port,
            user: configuration.user,
            password: configuration.password,
            secure: true,
            secureOptions: configuration.secureOptions
        });
        await client.cd(configuration.remoteDirectory);
        return client;
    } catch (error) {
        client.close();
        throw error;
    }
}

async function uploadFile(client, configuration, file) {
    const remoteParent = path.posix.dirname(file.relativePath);
    if (remoteParent !== ".") {
        const absoluteParent = path.posix.join(configuration.remoteDirectory, remoteParent);
        await client.ensureDir(absoluteParent);
    } else {
        await client.cd(configuration.remoteDirectory);
    }

    await client.uploadFrom(file.localPath, path.posix.basename(file.relativePath));
    await client.cd(configuration.remoteDirectory);
}

function requestUrl(url, redirectsRemaining = 3) {
    return new Promise((resolve, reject) => {
        const transport = url.protocol === "https:" ? https : http;
        const request = transport.get(url, {
            timeout: 15_000,
            headers: { "User-Agent": "SafeSpeak-deployment-verifier/1.0" }
        }, response => {
            const status = response.statusCode || 0;
            const location = response.headers.location;
            response.resume();

            if (status >= 300 && status < 400 && location && redirectsRemaining > 0) {
                resolve(requestUrl(new URL(location, url), redirectsRemaining - 1));
                return;
            }
            resolve({ status, contentType: response.headers["content-type"] || "" });
        });
        request.on("timeout", () => request.destroy(new Error("Request timed out.")));
        request.on("error", reject);
    });
}

async function verifyPublicWebsite() {
    const baseUrl = new URL(required("PUBLIC_BASE_URL"));
    if (baseUrl.protocol !== "https:") {
        throw new Error("PUBLIC_BASE_URL must use HTTPS.");
    }

    console.log("Verifying public pages...");
    for (const route of publicRoutes) {
        const result = await requestUrl(new URL(route, baseUrl));
        if (result.status !== 200) {
            throw new Error(`${route} returned HTTP ${result.status}.`);
        }
        if (!result.contentType.toLowerCase().includes("text/html")) {
            throw new Error(`${route} did not return HTML.`);
        }
        console.log(`  ${route.padEnd(16)} HTTP 200`);
    }
    console.log("Public verification passed.");
}

async function main() {
    loadEnv(envPath);

    const args = new Set(process.argv.slice(2));
    const allowedArguments = new Set(["--dry-run", "--check", "--verify-only"]);
    for (const argument of args) {
        if (!allowedArguments.has(argument)) throw new Error(`Unknown argument: ${argument}`);
    }
    if (args.size > 1) throw new Error("Choose only one deployment mode.");

    if (args.has("--verify-only")) {
        await verifyPublicWebsite();
        return;
    }

    const files = localFiles();
    if (args.has("--dry-run")) {
        printPlan(files, "DRY RUN — no network connection");
        return;
    }

    const configuration = transferConfiguration();
    printPlan(files, args.has("--check") ? "CONNECTION CHECK" : "UPLOAD");

    const client = await connect(configuration);
    try {
        if (args.has("--check")) {
            console.log("Secure connection, server identity, credentials, and remote directory verified.");
            return;
        }

        // The root index is last in the allowlist so visitors cannot receive a
        // page that refers to assets or routes which have not finished uploading.
        for (const file of files) {
            process.stdout.write(`Uploading ${file.relativePath}... `);
            await uploadFile(client, configuration, file);
            console.log("done");
        }
    } finally {
        client.close();
    }

    console.log("Upload complete. No remote files were deleted.");
    await verifyPublicWebsite();
}

main().catch(error => {
    const message = error && error.message ? error.message : String(error);
    console.error(`Deployment failed: ${message}`);
    process.exitCode = 1;
});
