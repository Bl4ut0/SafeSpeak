using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace TikFinityEmulator;

internal class Program
{
    private static readonly List<WebSocket> Clients = new();
    private static readonly object ClientsLock = new();
    private static HttpListener? _listener;
    private static bool _isRunning = true;
    private static CancellationTokenSource? _autoSimCts;
    private static readonly List<object> EventHistory = new();
    private static readonly object HistoryLock = new();

    static async Task Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        Console.Title = "TikFinity Emulator Dashboard — http://localhost:21213";

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("==================================================================");
        Console.WriteLine("   🎮 TikFinity Local Server & Web Dashboard (Port 21213)        ");
        Console.WriteLine("==================================================================");
        Console.ResetColor();

        int port = 21213;
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://localhost:{port}/");

        try
        {
            _listener.Start();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[✓] WebSocket Server: ws://localhost:{port}/");
            Console.WriteLine($"[✓] Web Test Dashboard: http://localhost:{port}/");
            Console.WriteLine("[*] Waiting for SafeSpeak client connection...\n");
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[!] Failed to bind to port {port}: {ex.Message}");
            Console.ResetColor();
            return;
        }

        _ = Task.Run(AcceptHttpAndWebSocketLoopAsync);

        PrintConsoleMenu();

        while (_isRunning)
        {
            Console.Write("\n[Option 1-8, D (open dashboard), Q (quit)]: ");
            string? input = Console.ReadLine()?.Trim().ToUpperInvariant();

            if (input == "Q")
            {
                _isRunning = false;
                break;
            }

            switch (input)
            {
                case "1": await SendCleanMessageAsync(); break;
                case "2": await SendEvasionAttackAsync(); break;
                case "3": await SendToxicMessageAsync(); break;
                case "4": await SendGiftEventAsync(); break;
                case "5": await SendFollowOrShareEventAsync(); break;
                case "6": ToggleAutoSimulation(); break;
                case "7": await SendSpamFloodAsync(); break;
                case "8": await SendCustomMessagePromptAsync(); break;
                case "D":
                    OpenBrowserDashboard($"http://localhost:{port}/");
                    break;
                case "M": PrintConsoleMenu(); break;
                default:
                    Console.WriteLine("Unknown command. Type 'M' for menu or 'D' to open the Web Dashboard.");
                    break;
            }
        }

        _listener.Stop();
    }

    private static void OpenBrowserDashboard(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
            Console.WriteLine($"[✓] Opened {url} in browser.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Could not auto-open browser: {ex.Message}. Please navigate to {url}");
        }
    }

    private static void PrintConsoleMenu()
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("\n--- Available Actions ---");
        Console.WriteLine(" [D] Open Interactive Web Dashboard in Browser (http://localhost:21213)");
        Console.WriteLine(" [1] Send Clean Chat Message");
        Console.WriteLine(" [2] Send Evasion Attack (Zero-width / Cyrillic / Spaced / Leetspeak)");
        Console.WriteLine(" [3] Send Subtle AI Toxicity / Threat");
        Console.WriteLine(" [4] Send Gift Event (Rose, Galaxy, TikTok Universe)");
        Console.WriteLine(" [5] Send Follow / Share Event");
        Console.WriteLine(" [6] Toggle Continuous Live Stream Simulation");
        Console.WriteLine(" [7] Send Rapid Spam Flood (30 msgs in 1 sec)");
        Console.WriteLine(" [8] Custom Message Console Prompt");
        Console.WriteLine(" [Q] Quit Emulator");
        Console.WriteLine("-------------------------");
        Console.ResetColor();
    }

    private static async Task AcceptHttpAndWebSocketLoopAsync()
    {
        while (_isRunning && _listener!.IsListening)
        {
            try
            {
                var context = await _listener.GetContextAsync();

                if (context.Request.IsWebSocketRequest)
                {
                    var wsContext = await context.AcceptWebSocketAsync(subProtocol: null);
                    var socket = wsContext.WebSocket;

                    lock (ClientsLock)
                    {
                        Clients.Add(socket);
                    }

                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"\n[+] SafeSpeak connected via WebSocket! (Active clients: {Clients.Count})");
                    Console.ResetColor();

                    _ = Task.Run(() => HandleWebSocketClientAsync(socket));
                }
                else
                {
                    _ = ProcessHttpRequestAsync(context);
                }
            }
            catch (HttpListenerException) { break; }
            catch (Exception ex)
            {
                Console.WriteLine($"[Listener Error]: {ex.Message}");
            }
        }
    }

    private static async Task ProcessHttpRequestAsync(HttpListenerContext context)
    {
        var req = context.Request;
        var resp = context.Response;

        // Allow CORS
        resp.Headers.Add("Access-Control-Allow-Origin", "*");
        resp.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
        resp.Headers.Add("Access-Control-Allow-Headers", "Content-Type");

        if (req.HttpMethod == "OPTIONS")
        {
            resp.StatusCode = 200;
            resp.Close();
            return;
        }

        string path = req.Url?.AbsolutePath.ToLowerInvariant() ?? "/";

        try
        {
            if (path == "/" || path == "/index.html")
            {
                byte[] htmlBytes = Encoding.UTF8.GetBytes(GetDashboardHtml());
                resp.ContentType = "text/html; charset=utf-8";
                resp.StatusCode = 200;
                await resp.OutputStream.WriteAsync(htmlBytes);
            }
            else if (path == "/api/status")
            {
                int count;
                lock (ClientsLock) count = Clients.Count(c => c.State == WebSocketState.Open);

                var status = new
                {
                    isRunning = true,
                    connectedClients = count,
                    isSimulating = _autoSimCts != null,
                    historyCount = EventHistory.Count
                };

                byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(status));
                resp.ContentType = "application/json";
                resp.StatusCode = 200;
                await resp.OutputStream.WriteAsync(bytes);
            }
            else if (path == "/api/send-clean" && req.HttpMethod == "POST")
            {
                await SendCleanMessageAsync();
                await WriteJsonResponseAsync(resp, new { success = true, message = "Clean message sent" });
            }
            else if (path == "/api/send-attack" && req.HttpMethod == "POST")
            {
                await SendEvasionAttackAsync();
                await WriteJsonResponseAsync(resp, new { success = true, message = "Evasion attack sent" });
            }
            else if (path == "/api/send-toxic" && req.HttpMethod == "POST")
            {
                await SendToxicMessageAsync();
                await WriteJsonResponseAsync(resp, new { success = true, message = "Toxic comment sent" });
            }
            else if (path == "/api/send-gift" && req.HttpMethod == "POST")
            {
                await SendGiftEventAsync();
                await WriteJsonResponseAsync(resp, new { success = true, message = "Gift event sent" });
            }
            else if (path == "/api/send-follow" && req.HttpMethod == "POST")
            {
                await SendFollowOrShareEventAsync();
                await WriteJsonResponseAsync(resp, new { success = true, message = "Follow/Share event sent" });
            }
            else if (path == "/api/toggle-simulation" && req.HttpMethod == "POST")
            {
                ToggleAutoSimulation();
                await WriteJsonResponseAsync(resp, new { success = true, isSimulating = _autoSimCts != null });
            }
            else if (path == "/api/spam-flood" && req.HttpMethod == "POST")
            {
                _ = Task.Run(SendSpamFloodAsync);
                await WriteJsonResponseAsync(resp, new { success = true, message = "Spam flood triggered" });
            }
            else if (path == "/api/send-custom" && req.HttpMethod == "POST")
            {
                using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
                string body = await reader.ReadToEndAsync();
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                string text = root.GetProperty("text").GetString() ?? "";
                string user = root.TryGetProperty("user", out var u) ? u.GetString() ?? "StreamFan" : "StreamFan";
                bool isSub = root.TryGetProperty("isSub", out var s) && s.GetBoolean();
                bool isMod = root.TryGetProperty("isMod", out var m) && m.GetBoolean();

                var chatEvent = new
                {
                    @event = "chat",
                    data = new
                    {
                        comment = text,
                        uniqueId = user.ToLowerInvariant().Replace(" ", "_"),
                        nickname = user,
                        userId = "12345" + new Random().Next(100, 999),
                        isSubscriber = isSub,
                        isModerator = isMod,
                        followRole = isMod ? 3 : (isSub ? 2 : 1)
                    }
                };

                await BroadcastEventAsync(chatEvent);
                await WriteJsonResponseAsync(resp, new { success = true, message = "Custom message sent" });
            }
            else if (path == "/api/history")
            {
                List<object> list;
                lock (HistoryLock) list = EventHistory.TakeLast(50).Reverse().ToList();

                byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(list));
                resp.ContentType = "application/json";
                resp.StatusCode = 200;
                await resp.OutputStream.WriteAsync(bytes);
            }
            else
            {
                resp.StatusCode = 404;
            }
        }
        catch (Exception ex)
        {
            resp.StatusCode = 500;
            byte[] err = Encoding.UTF8.GetBytes(ex.Message);
            await resp.OutputStream.WriteAsync(err);
        }
        finally
        {
            try { resp.Close(); } catch { }
        }
    }

    private static async Task WriteJsonResponseAsync(HttpListenerResponse resp, object payload)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
        resp.ContentType = "application/json";
        resp.StatusCode = 200;
        await resp.OutputStream.WriteAsync(bytes);
    }

    private static async Task HandleWebSocketClientAsync(WebSocket socket)
    {
        var buffer = new byte[4096];
        while (socket.State == WebSocketState.Open && _isRunning)
        {
            try
            {
                var result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed", CancellationToken.None);
                    break;
                }
            }
            catch { break; }
        }

        lock (ClientsLock)
        {
            Clients.Remove(socket);
        }

        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.WriteLine($"\n[-] SafeSpeak client disconnected. (Remaining: {Clients.Count})");
        Console.ResetColor();
    }

    private static async Task BroadcastEventAsync(object payload)
    {
        lock (HistoryLock)
        {
            EventHistory.Add(payload);
            if (EventHistory.Count > 200) EventHistory.RemoveAt(0);
        }

        string json = JsonSerializer.Serialize(payload);
        byte[] bytes = Encoding.UTF8.GetBytes(json);

        List<WebSocket> active;
        lock (ClientsLock)
        {
            active = Clients.Where(c => c.State == WebSocketState.Open).ToList();
        }

        if (active.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("    (Notice: SafeSpeak is not connected to ws://localhost:21213)");
            Console.ResetColor();
            return;
        }

        foreach (var client in active)
        {
            try
            {
                await client.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
            }
            catch { }
        }
    }

    private static readonly string[] CleanUsers = { "PixelGamer", "CozyStreamer", "Sarah_Star", "GamerBro99", "CoffeeLover", "AeroViper" };
    private static readonly string[] CleanComments = {
        "Hey everyone! Great stream today!",
        "What game are we playing next?",
        "Awesome gameplay, that shot was insane!",
        "Hope you have a fantastic evening streamer!",
        "First time here, just followed!",
        "Check out that high score! Let's go!"
    };

    private static async Task SendCleanMessageAsync()
    {
        var rnd = new Random();
        string user = CleanUsers[rnd.Next(CleanUsers.Length)];
        string text = CleanComments[rnd.Next(CleanComments.Length)];
        bool isSub = rnd.Next(2) == 1;

        var chatEvent = new
        {
            @event = "chat",
            data = new
            {
                comment = text,
                uniqueId = user.ToLowerInvariant(),
                nickname = user,
                userId = "100" + rnd.Next(1000, 9999),
                isSubscriber = isSub,
                isModerator = false,
                followRole = 1
            }
        };

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"\n[>] Sent Clean Chat: [{user}] \"{text}\" (Sub: {isSub})");
        Console.ResetColor();

        await BroadcastEventAsync(chatEvent);
    }

    private static readonly (string attackName, string comment)[] Attacks = {
        ("Zero-Width Space Evasion", "f\u200Bu\u200Bc\u200Bk this streamer"),
        ("Cyrillic Homoglyph Injection", "You are a f\u0430ggot"),
        ("Spaced Letter Evasion", "k y s right now"),
        ("Leetspeak Symbol Substitution", "k1ll your$elf loser"),
        ("Zalgo Combining Diacritics", "h̵̡e̸l̶l̴o̸ idiot"),
        ("URL Spam", "Get free gift cards at https://scam-site.xyz/win free free free"),
        ("Character Stretch", "fuuuuuuuuuck off")
    };

    private static async Task SendEvasionAttackAsync()
    {
        var rnd = new Random();
        var (attackName, comment) = Attacks[rnd.Next(Attacks.Length)];

        var chatEvent = new
        {
            @event = "chat",
            data = new
            {
                comment = comment,
                uniqueId = "attacker_" + rnd.Next(10, 99),
                nickname = "TrollUser",
                userId = "666" + rnd.Next(1000, 9999),
                isSubscriber = false,
                isModerator = false,
                followRole = 0
            }
        };

        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine($"\n[!] Sent {attackName}: \"{comment}\"");
        Console.ResetColor();

        await BroadcastEventAsync(chatEvent);
    }

    private static readonly string[] ToxicComments = {
        "Nobody in this chat actually likes you, you are a complete waste of space.",
        "You're so pathetic and disgusting at this game, go away and die.",
        "Why do you even bother streaming? Literally trash.",
        "I hope something terrible happens to you today."
    };

    private static async Task SendToxicMessageAsync()
    {
        var rnd = new Random();
        string text = ToxicComments[rnd.Next(ToxicComments.Length)];

        var chatEvent = new
        {
            @event = "chat",
            data = new
            {
                comment = text,
                uniqueId = "toxic_viewer_" + rnd.Next(10, 99),
                nickname = "ToxicViewer",
                userId = "999" + rnd.Next(1000, 9999),
                isSubscriber = false,
                isModerator = false,
                followRole = 0
            }
        };

        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"\n[!] Sent Subtle AI Toxicity: \"{text}\"");
        Console.ResetColor();

        await BroadcastEventAsync(chatEvent);
    }

    private static readonly (string name, int diamonds)[] GiftTypes = {
        ("Rose", 1),
        ("TikTok", 1),
        ("Finger Heart", 5),
        ("Corgi", 299),
        ("Galaxy", 1000),
        ("Lion", 29999),
        ("TikTok Universe", 34999)
    };

    private static async Task SendGiftEventAsync()
    {
        var rnd = new Random();
        var gift = GiftTypes[rnd.Next(GiftTypes.Length)];
        string user = CleanUsers[rnd.Next(CleanUsers.Length)];
        int count = gift.diamonds <= 5 ? rnd.Next(1, 10) : 1;

        var giftEvent = new
        {
            @event = "gift",
            data = new
            {
                giftName = gift.name,
                giftCount = count,
                diamondCount = gift.diamonds * count,
                uniqueId = user.ToLowerInvariant(),
                nickname = user,
                userId = "200" + rnd.Next(1000, 9999)
            }
        };

        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"\n[🎁] Sent Gift: [{user}] sent {count}x {gift.name} ({gift.diamonds * count} Diamonds)");
        Console.ResetColor();

        await BroadcastEventAsync(giftEvent);
    }

    private static async Task SendFollowOrShareEventAsync()
    {
        var rnd = new Random();
        string user = CleanUsers[rnd.Next(CleanUsers.Length)];
        bool isFollow = rnd.Next(2) == 1;

        var evt = new
        {
            @event = isFollow ? "follow" : "share",
            data = new
            {
                uniqueId = user.ToLowerInvariant(),
                nickname = user,
                userId = "300" + rnd.Next(1000, 9999)
            }
        };

        Console.ForegroundColor = ConsoleColor.Blue;
        Console.WriteLine($"\n[⭐] Sent {(isFollow ? "Follow" : "Share")} Event: [{user}]");
        Console.ResetColor();

        await BroadcastEventAsync(evt);
    }

    private static void ToggleAutoSimulation()
    {
        if (_autoSimCts != null)
        {
            _autoSimCts.Cancel();
            _autoSimCts.Dispose();
            _autoSimCts = null;
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("\n[■] Simulation STOPPED.");
            Console.ResetColor();
            return;
        }

        _autoSimCts = new CancellationTokenSource();
        var token = _autoSimCts.Token;

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("\n[▶] Simulation STARTED (Events every 1.5s).");
        Console.ResetColor();

        _ = Task.Run(async () =>
        {
            var rnd = new Random();
            while (!token.IsCancellationRequested)
            {
                int choice = rnd.Next(10);
                if (choice < 5) await SendCleanMessageAsync();
                else if (choice < 7) await SendGiftEventAsync();
                else if (choice < 8) await SendFollowOrShareEventAsync();
                else if (choice < 9) await SendEvasionAttackAsync();
                else await SendToxicMessageAsync();

                try { await Task.Delay(1500, token); } catch { break; }
            }
        }, token);
    }

    private static async Task SendSpamFloodAsync()
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("\n[⚡] Broadcasting Spam Flood: 30 messages in 1 second...");
        Console.ResetColor();

        for (int i = 1; i <= 30; i++)
        {
            var chatEvent = new
            {
                @event = "chat",
                data = new
                {
                    comment = $"Spam message #{i} checking rate limiting and queue pressure!",
                    uniqueId = "spammer_" + (i % 3),
                    nickname = "Spammer " + (i % 3),
                    userId = "500" + i,
                    isSubscriber = false,
                    isModerator = false,
                    followRole = 0
                }
            };

            await BroadcastEventAsync(chatEvent);
            await Task.Delay(30);
        }

        Console.WriteLine("[✓] Spam flood complete.");
    }

    private static async Task SendCustomMessagePromptAsync()
    {
        Console.Write("\nEnter Comment Text: ");
        string? text = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(text)) return;

        Console.Write("Enter Username (Default: StreamFan): ");
        string? user = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(user)) user = "StreamFan";

        Console.Write("Is Subscriber? (y/N): ");
        bool isSub = Console.ReadLine()?.Trim().ToLowerInvariant() == "y";

        var chatEvent = new
        {
            @event = "chat",
            data = new
            {
                comment = text,
                uniqueId = user.ToLowerInvariant().Replace(" ", "_"),
                nickname = user,
                userId = "12345678",
                isSubscriber = isSub,
                isModerator = false,
                followRole = isSub ? 2 : 1
            }
        };

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"\n[>] Sent Custom Chat: [{user}] \"{text}\"");
        Console.ResetColor();

        await BroadcastEventAsync(chatEvent);
    }

    private static string GetDashboardHtml()
    {
        return """
        <!DOCTYPE html>
        <html lang="en">
        <head>
            <meta charset="UTF-8">
            <meta name="viewport" content="width=device-width, initial-scale=1.0">
            <title>TikFinity Event Emulator Dashboard</title>
            <style>
                :root {
                    --bg: #0d1117;
                    --panel: #161b22;
                    --panel-border: #30363d;
                    --primary: #00e7f9;
                    --primary-hover: #00bcd4;
                    --success: #2ea043;
                    --danger: #da3633;
                    --warning: #d29922;
                    --purple: #a371f7;
                    --text: #f0f6fc;
                    --text-muted: #8b949e;
                }
                * { box-sizing: border-box; margin: 0; padding: 0; font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif; }
                body { background: var(--bg); color: var(--text); padding: 24px; }
                .header { display: flex; justify-content: space-between; align-items: center; border-bottom: 1px solid var(--panel-border); padding-bottom: 16px; margin-bottom: 24px; }
                .header h1 { font-size: 24px; color: var(--primary); display: flex; align-items: center; gap: 10px; }
                .status-badge { padding: 6px 14px; border-radius: 20px; font-weight: bold; font-size: 13px; background: #238636; color: #fff; display: flex; align-items: center; gap: 8px; }
                .grid { display: grid; grid-template-columns: 1fr 1fr; gap: 20px; }
                .card { background: var(--panel); border: 1px solid var(--panel-border); border-radius: 8px; padding: 20px; }
                .card h2 { font-size: 18px; margin-bottom: 16px; color: var(--text); border-bottom: 1px solid var(--panel-border); padding-bottom: 8px; }
                .btn-grid { display: grid; grid-template-columns: repeat(2, 1fr); gap: 12px; }
                button { background: #21262d; border: 1px solid var(--panel-border); color: var(--text); padding: 12px 16px; border-radius: 6px; font-size: 14px; font-weight: 600; cursor: pointer; transition: all 0.15s ease; text-align: left; display: flex; align-items: center; gap: 10px; }
                button:hover { background: #30363d; border-color: var(--primary); transform: translateY(-1px); }
                button.primary { background: #1f6feb; border-color: #388bfd; }
                button.danger { background: #b62324; border-color: #da3633; }
                button.success { background: #238636; border-color: #2ea043; }
                button.purple { background: #6e40c9; border-color: #8957e5; }
                button.warning { background: #9e6a03; border-color: #bb8009; }
                .form-group { margin-bottom: 14px; }
                label { display: block; font-size: 13px; color: var(--text-muted); margin-bottom: 6px; font-weight: 600; }
                input[type="text"], select { width: 100%; background: #0d1117; border: 1px solid var(--panel-border); border-radius: 6px; padding: 10px 12px; color: #fff; font-size: 14px; }
                input[type="text"]:focus { outline: none; border-color: var(--primary); }
                .checkboxes { display: flex; gap: 16px; margin: 10px 0; }
                .checkboxes label { display: flex; align-items: center; gap: 6px; cursor: pointer; color: var(--text); }
                .feed-container { max-height: 480px; overflow-y: auto; background: #0d1117; border: 1px solid var(--panel-border); border-radius: 6px; padding: 12px; }
                .feed-item { padding: 8px 12px; border-bottom: 1px solid #21262d; font-size: 13px; display: flex; justify-content: space-between; align-items: center; }
                .feed-item:last-child { border-bottom: none; }
                .tag { font-size: 10px; font-weight: bold; padding: 2px 6px; border-radius: 4px; text-transform: uppercase; }
                .tag-chat { background: #1f6feb; color: #fff; }
                .tag-gift { background: #d29922; color: #000; }
                .tag-follow { background: #8957e5; color: #fff; }
            </style>
        </head>
        <body>
            <div class="header">
                <h1>🎮 TikFinity WebSocket Emulator</h1>
                <div class="status-badge" id="clientBadge">🟢 SafeSpeak Clients: 0</div>
            </div>

            <div class="grid">
                <!-- Left Column: Trigger Actions -->
                <div>
                    <div class="card" style="margin-bottom: 20px;">
                        <h2>⚡ Quick Scenario Triggers</h2>
                        <div class="btn-grid">
                            <button class="primary" onclick="sendApi('/api/send-clean')">
                                💬 Clean Chat Message
                            </button>
                            <button class="danger" onclick="sendApi('/api/send-attack')">
                                🛡️ Evasion Attack (Zero-Width/Cyrillic)
                            </button>
                            <button class="purple" onclick="sendApi('/api/send-toxic')">
                                🧠 Subtle AI Toxicity
                            </button>
                            <button class="warning" onclick="sendApi('/api/send-gift')">
                                🎁 Send TikTok Gift (Galaxy/Rose)
                            </button>
                            <button onclick="sendApi('/api/send-follow')">
                                ⭐ Follow / Share Event
                            </button>
                            <button class="danger" onclick="sendApi('/api/spam-flood')">
                                🌊 Spam Flood (30 msgs)
                            </button>
                        </div>
                        <div style="margin-top: 16px;">
                            <button id="simBtn" class="success" style="width: 100%; justify-content: center;" onclick="sendApi('/api/toggle-simulation')">
                                ▶ Start Continuous Live Simulation
                            </button>
                        </div>
                    </div>

                    <div class="card">
                        <h2>✍️ Custom Chat Message Builder</h2>
                        <div class="form-group">
                            <label>Author Display Name</label>
                            <input type="text" id="custUser" value="TikTokFan42">
                        </div>
                        <div class="form-group">
                            <label>Comment Text</label>
                            <input type="text" id="custText" placeholder="Type custom chat comment here...">
                        </div>
                        <div class="checkboxes">
                            <label><input type="checkbox" id="custSub"> Is Subscriber</label>
                            <label><input type="checkbox" id="custMod"> Is Moderator</label>
                        </div>
                        <button class="primary" style="width: 100%; justify-content: center;" onclick="sendCustom()">
                            🚀 Inject Custom Message
                        </button>
                    </div>
                </div>

                <!-- Right Column: Live Event Broadcast Feed -->
                <div class="card">
                    <h2>📡 Live Broadcast Feed (Port 21213)</h2>
                    <div class="feed-container" id="feedList">
                        <div style="color: var(--text-muted); text-align: center; padding: 20px;">No events sent yet. Click any button on the left!</div>
                    </div>
                </div>
            </div>

            <script>
                async function sendApi(endpoint) {
                    try {
                        const res = await fetch(endpoint, { method: 'POST' });
                        const data = await res.json();
                        updateFeed();
                    } catch (e) {
                        alert('Error communicating with server: ' + e);
                    }
                }

                async function sendCustom() {
                    const text = document.getElementById('custText').value;
                    const user = document.getElementById('custUser').value;
                    const isSub = document.getElementById('custSub').checked;
                    const isMod = document.getElementById('custMod').checked;

                    if (!text) return;

                    try {
                        await fetch('/api/send-custom', {
                            method: 'POST',
                            headers: { 'Content-Type': 'application/json' },
                            body: JSON.stringify({ text, user, isSub, isMod })
                        });
                        document.getElementById('custText').value = '';
                        updateFeed();
                    } catch (e) {
                        alert('Error: ' + e);
                    }
                }

                async function updateStatus() {
                    try {
                        const res = await fetch('/api/status');
                        const data = await res.json();
                        document.getElementById('clientBadge').innerText = data.connectedClients > 0
                            ? `🟢 SafeSpeak Connected (${data.connectedClients})`
                            : `🔴 Waiting for SafeSpeak...`;
                        document.getElementById('clientBadge').style.background = data.connectedClients > 0 ? '#238636' : '#da3633';

                        const simBtn = document.getElementById('simBtn');
                        if (data.isSimulating) {
                            simBtn.innerText = '■ Stop Continuous Simulation';
                            simBtn.className = 'danger';
                        } else {
                            simBtn.innerText = '▶ Start Continuous Live Simulation';
                            simBtn.className = 'success';
                        }
                    } catch (e) {}
                }

                async function updateFeed() {
                    try {
                        const res = await fetch('/api/history');
                        const list = await res.json();
                        const feed = document.getElementById('feedList');
                        if (list.length === 0) return;

                        feed.innerHTML = list.map(item => {
                            const evt = item.event;
                            const data = item.data;
                            let tagClass = 'tag-chat';
                            let detail = '';

                            if (evt === 'chat') {
                                detail = `<strong>${data.nickname || data.uniqueId}</strong>: ${data.comment}`;
                            } else if (evt === 'gift') {
                                tagClass = 'tag-gift';
                                detail = `<strong>${data.nickname}</strong> sent ${data.giftCount}x ${data.giftName} (${data.diamondCount} 💎)`;
                            } else {
                                tagClass = 'tag-follow';
                                detail = `<strong>${data.nickname}</strong> triggered ${evt}`;
                            }

                            return `<div class="feed-item">
                                <div><span class="tag ${tagClass}">${evt}</span> <span style="margin-left: 8px;">${detail}</span></div>
                                <span style="color: var(--text-muted); font-size: 11px;">ws://localhost:21213</span>
                            </div>`;
                        }).join('');
                    } catch (e) {}
                }

                setInterval(updateStatus, 1500);
                setInterval(updateFeed, 1500);
                updateStatus();
                updateFeed();
            </script>
        </body>
        </html>
        """;
    }
}
