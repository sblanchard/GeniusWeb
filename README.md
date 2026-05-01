# GeniusOnline

Mobile-friendly web control panel for FlexRadio Genius accessories — RotatorGenius, AntennaGenius, PowerGenius, TunerGenius — all from one ASP.NET Core app.

## Stack

- **Backend**: ASP.NET Core 10 (Minimal API + SignalR)
- **UI**: Blazor Server + MudBlazor, mobile-first, PWA-installable
- **Realtime**: SignalR (`/hubs/devices`)
- **Persistence**: SQLite via EF Core
- **Auth**: cookie auth + single-password gate (PBKDF2-SHA256)

The four FlexRadio Genius client libraries (`RotatorGenius.Communication`, `AntennaGenius.Core`, `PowerGenius.Core`, `TunerGenius.TgLib`) live under `vendor/` as **git submodules**. Clone with:

```bash
git clone --recurse-submodules https://github.com/sblanchard/GeniusOnline.git
# or after a normal clone
git submodule update --init --recursive
```

## Solution layout

```
src/
  GeniusOnline.Contracts/   DTOs (DeviceKind, DeviceStatusFrame, DeviceCommand…)
  GeniusOnline.Devices/     IDeviceAdapter + per-kind adapters + DeviceRegistry hosted service
  GeniusOnline.Data/        EF Core DbContext, entities (Preset, UserSetting, AuditLog)
  GeniusOnline.Web/         ASP.NET host, Blazor components, SignalR hub, auth
tests/
  GeniusOnline.Devices.Tests/
  GeniusOnline.Web.Tests/
tools/
  GeniusOnline.Tools.PasswordHash/   tiny CLI for generating Auth:PasswordHash values
scripts/
  publish.sh              multi-RID self-contained publish
  geniusonline.service    systemd unit
```

## Run locally

Dev (auto-listens on `0.0.0.0:9050`):

```bash
dotnet run --project src/GeniusOnline.Web
```

Open http://localhost:9050 — you'll be redirected to `/login`. The default seeded password is **`geniusonline`** — change it before exposing the host (see "Changing the password" below).

## Configure devices

Edit `src/GeniusOnline.Web/appsettings.json` (or use a Production `appsettings.Production.json` next to the deployed binary):

```json
{
  "Devices": {
    "Devices": [
      { "Id": "rot-1", "Kind": "Rotator", "DisplayName": "Tower",  "Address": "192.168.1.50", "Port": 9006 },
      { "Id": "ant-1", "Kind": "Antenna", "DisplayName": "Switch", "Address": "192.168.1.51" },
      { "Id": "pwr-1", "Kind": "Power",   "DisplayName": "Amp",    "Address": "192.168.1.52" },
      { "Id": "tun-1", "Kind": "Tuner",   "DisplayName": "Tuner",  "Address": "192.168.1.53" }
    ]
  }
}
```

`Kind` values: `Rotator` | `Antenna` | `Power` | `Tuner`. The registry connects with exponential backoff on startup; offline devices show as `Disconnected` on the dashboard and reconnect automatically.

## Changing the password

```bash
dotnet run --project tools/GeniusOnline.Tools.PasswordHash -- "my new password"
# → pbkdf2.sha256.210000.<salt>.<hash>
```

Paste the line into `Auth:PasswordHash` in `appsettings.json`. Restart the app.

To disable the gate entirely (LAN-only, single-trusted-network):

```json
"Auth": { "Mode": "Disabled" }
```

## Tests

```bash
dotnet test
```

## Deploy

### Docker / Compose

```bash
git clone --recurse-submodules https://github.com/sblanchard/GeniusOnline.git
cd GeniusOnline
docker compose up -d --build
```

The compose file uses `network_mode: host` so UDP device discovery works on the LAN — Docker's bridge network silently drops broadcasts. SQLite db lives in `./data/` next to the compose file.

### systemd (e.g. on a Raspberry Pi at the shack)

```bash
./scripts/publish.sh
sudo useradd -r geniusonline
sudo install -d -o geniusonline -g geniusonline /opt/geniusonline /var/lib/geniusonline
sudo cp -r publish/linux-arm64/* /opt/geniusonline/
sudo cp scripts/geniusonline.service /etc/systemd/system/
sudo systemctl enable --now geniusonline
```

### Reverse proxy / HTTPS / remote access

Front the app with Caddy / nginx / Tailscale Funnel. SignalR uses a single WebSocket — make sure the proxy passes `Upgrade` headers.

## Security notes

- Cookies: `HttpOnly`, `SameSite=Lax`, `Secure` follows the request scheme. Run behind HTTPS in production.
- Rate-limited login: 5 attempts/min/IP, generic error message, audit-logged.
- The Web project's anti-forgery tokens cover the login form. Service-worker is scoped to skip `/login`, `/logout`, `/_blazor`, `/hubs`.
- Single device, single client of record: the web app expects exclusive ownership of each device's TCP slot. Running both this app and the desktop variant against the same hardware is unsupported.
