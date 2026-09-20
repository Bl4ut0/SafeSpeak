"use strict";
// Creates a one-use test authorization and uploads only a synthetic local fixture.
const fs = require("fs"), path = require("path"), crypto = require("crypto");
const { Readable, Writable } = require("stream");
const { loadEnv, transferConfiguration, connect } = require("./deploy");
async function main() {
    loadEnv(path.join(__dirname, ".env"));
    const repository = path.resolve(__dirname, "../..");
    const fixtureRoot = path.join(repository, "local-deployment/safespeak-diagnostics/test-storage");
    const roots = fs.readdirSync(fixtureRoot).map(name => path.join(fixtureRoot, name))
        .filter(directory => fs.statSync(directory).isDirectory())
        .sort((a,b) => fs.statSync(b).mtimeMs - fs.statSync(a).mtimeMs);
    const fixture = roots.flatMap(directory => {
        const uploads = path.join(directory, "uploads");
        return fs.existsSync(uploads) ? fs.readdirSync(uploads).filter(name => fs.statSync(path.join(uploads,name)).isDirectory())
            .flatMap(host => fs.readdirSync(path.join(uploads,host)).filter(name => name.endsWith(".zip")).map(name => path.join(uploads,host,name))) : [];
    })[0];
    if (!fixture) throw new Error("Run the local receiver integration tests first to generate the synthetic ZIP.");
    const payload = fs.readFileSync(fixture);
    if (payload.length > 65536) throw new Error("Synthetic test package exceeds 64 KB.");
    const config = transferConfiguration(), client = await connect(config);
    const root = path.posix.join(path.posix.dirname(config.remoteDirectory), "diagnostics-private");
    const base = new URL("/diagnostics/index.php", process.env.PUBLIC_BASE_URL);
    if (base.protocol !== "https:" || base.hostname !== "safespeak.bl4ut0.dev") throw new Error("Unexpected diagnostics target.");
    const hash = value => crypto.createHash("sha256").update(value).digest("hex");
    const code = crypto.randomBytes(32).toString("hex");
    const cleanup = [];
    try {
        const host = path.basename(path.dirname(fixture));
        const signature = crypto.createHmac('sha256', 'SafeSpeak-diagnostics-app-signature-v1')
            .update(host + '\n' + payload.length + '\n' + hash(payload)).digest('hex');
        const handshake = await fetch(base + "?action=handshake", {
            method:"POST", redirect:"error", signal:AbortSignal.timeout(15000),
            headers:{"Content-Type":"application/json"},
            body:JSON.stringify({hostId:host,bytes:payload.length,sha256:hash(payload),signatureVersion:1,signature})
        });
        console.log(`Live handshake HTTP ${handshake.status}.`);
        if (handshake.status !== 200) throw new Error(`Handshake returned HTTP ${handshake.status}.`);
        const {token} = await handshake.json();
        if (!/^[a-f0-9]{64}$/.test(token)) throw new Error("Invalid ticket.");
        cleanup.push(path.posix.join(root,"tickets",hash(token)+".json"));
        const upload = await fetch(base + "?action=upload", {
            method:"POST", redirect:"error", signal:AbortSignal.timeout(15000),
            headers:{"Content-Type":"application/zip","Authorization":"Bearer "+token,"X-SafeSpeak-Upload-Ticket":token},body:payload
        });
        console.log(`Live upload HTTP ${upload.status}.`);
        if (upload.status !== 201) throw new Error(`Upload returned HTTP ${upload.status}.`);
        const {id, hostId, receivedAt} = await upload.json();
        if (!/^[a-f0-9]{8}-(?:[a-f0-9]{4}-){3}[a-f0-9]{12}$/.test(id)) throw new Error("Invalid receipt.");
        if (!/^[a-f0-9]{8}-(?:[a-f0-9]{4}-){3}[a-f0-9]{12}$/.test(hostId) || !/^\d{8}T\d{6}Z$/.test(receivedAt)) throw new Error("Invalid sorted receipt.");
        const prefix = path.posix.join(root,"uploads",hostId,receivedAt+"-"+id);
        const remoteZip = prefix+".zip";
        cleanup.push(remoteZip,prefix+".json");
        const chunks = [];
        await client.downloadTo(new Writable({write(chunk,encoding,done){chunks.push(chunk);done();}}), remoteZip);
        if (hash(Buffer.concat(chunks)) !== hash(payload)) throw new Error("Stored package hash mismatch.");
        console.log("PASS: live HTTPS handshake, ZIP upload, receipt and privately stored file hash verified.");
    } finally {
        for (const file of cleanup) await client.remove(file, true);
        client.close();
        console.log("Synthetic test authorization and uploaded files removed.");
    }
}
main().catch(() => { console.error("Receiver smoke test failed; no credentials were printed."); process.exitCode=1; });
