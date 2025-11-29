# AI Chaos Brain (C# Edition)

The C# version of the AI Chaos Brain - a server that receives chaos commands and generates Lua code for Garry's Mod.

## Features

- 🤖 **AI Code Generation** - Uses OpenRouter API to generate Lua code from natural language requests
- 📺 **Twitch Integration** - OAuth login and chat listener for Twitch commands
- 🎬 **YouTube Integration** - OAuth login and Super Chat listener for YouTube Live
- 🎮 **Web Control Panel** - Easy-to-use web interface for sending commands
- 📜 **Command History** - Track, repeat, and undo previous commands
- 🔒 **Safety Features** - URL filtering, changelevel blocking, and cooldowns

## Quick Start

### Prerequisites

- [.NET 9.0 SDK](https://dotnet.microsoft.com/download) or later
- An [OpenRouter](https://openrouter.ai/) API key

### Running the Brain

```bash
cd AIChaos.Brain
dotnet run
```

The server will start on `http://localhost:5000`

Open your browser to:
- `http://localhost:5000/` - Control Panel
- `http://localhost:5000/#/setup` - Setup & Configuration

## Setup Guide

### 1. OpenRouter API (Required)

1. Go to [openrouter.ai/keys](https://openrouter.ai/keys)
2. Create an account and generate an API key
3. Enter the API key in the Setup page

### 2. Twitch Integration (Optional)

1. Go to [dev.twitch.tv/console](https://dev.twitch.tv/console)
2. Create a new application
3. Set the OAuth Redirect URL to: `http://localhost:5000/api/setup/twitch/callback`
4. Copy the Client ID and Client Secret to the Setup page
5. Click "Login with Twitch" to authenticate
6. Click "Start Listening" to begin receiving chat commands

#### Twitch Settings
- **Channel**: Your Twitch channel name
- **Chat Command**: The command prefix (default: `!chaos`)
- **Require Bits**: Only process commands with bits
- **Min Bits**: Minimum bits required

### 3. YouTube Integration (Optional)

1. Go to [Google Cloud Console](https://console.cloud.google.com/)
2. Create a new project
3. Enable the YouTube Data API v3
4. Create OAuth 2.0 credentials (Web Application)
5. Set the redirect URI to: `http://localhost:5000/api/setup/youtube/callback`
6. Copy the Client ID and Client Secret to the Setup page
7. Click "Login with YouTube" to authenticate
8. Enter your live stream's Video ID and click "Start Listening"

#### YouTube Settings
- **Video ID**: The video ID from your live stream URL (e.g., `dQw4w9WgXcQ`)
- **Min Super Chat**: Minimum Super Chat amount to trigger commands
- **Allow Regular Chat**: Also process non-donation chat messages

## GMod Setup

The GMod addon in the `lua/` folder needs to connect to this brain. Make sure:

1. The brain is running and accessible
2. Update `lua/autorun/ai_chaos_controller.lua` with the correct URL
3. For public access, use a tunnel service like ngrok or localtunnel

## API Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/` | GET | Web control panel |
| `/poll` | POST | GMod polls for commands |
| `/trigger` | POST | Send a chaos command |
| `/api/history` | GET | Get command history |
| `/api/repeat` | POST | Repeat a previous command |
| `/api/undo` | POST | Undo a command |
| `/api/force_undo` | POST | AI-generated force undo |
| `/api/setup/status` | GET | Get setup status |
| `/api/setup/twitch/auth-url` | GET | Get Twitch OAuth URL |
| `/api/setup/youtube/auth-url` | GET | Get YouTube OAuth URL |

## Configuration

Settings are stored in `settings.json` (auto-generated on first run).

You can also configure via `appsettings.json`:

```json
{
  "AIChaos": {
    "OpenRouter": {
      "BaseUrl": "https://openrouter.ai/api/v1",
      "Model": "anthropic/claude-sonnet-4.5"
    },
    "Safety": {
      "BlockUrls": true,
      "AllowedDomains": ["i.imgur.com", "imgur.com"]
    }
  }
}
```

## Development

```bash
# Restore dependencies
dotnet restore

# Build
dotnet build

# Run in development mode
dotnet run

# Publish for production
dotnet publish -c Release
```

## Comparison with Python Version

| Feature | Python | C# |
|---------|--------|-----|
| Runtime | Python 3.x | .NET 9.0 |
| Web Framework | Flask | ASP.NET Core |
| Twitch Auth | Manual token | OAuth flow |
| YouTube Auth | Manual token | OAuth flow |
| Configuration | Text files | JSON + Web UI |
| Image Scanning | ✅ (EasyOCR, BLIP) | ❌ (not ported) |

The C# version focuses on easy setup with OAuth authentication, while the Python version includes advanced image scanning features.

## Troubleshooting

### "Failed to connect to brain"
- Make sure the brain is running on port 5000
- Check firewall settings

### Twitch not receiving messages
- Verify OAuth token is valid (re-authenticate if needed)
- Make sure channel name is correct
- Check cooldown settings

### YouTube "Invalid video ID"
- The stream must be actively live
- Use the video ID from the URL, not the channel ID
- Make sure you're authenticated with the correct account

## License

See the main repository for license information.
