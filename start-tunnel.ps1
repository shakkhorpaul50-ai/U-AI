# Start a Cloudflare quick tunnel to the local app and point the Worker at it.
# Usage: .\start-tunnel.ps1
# If CLOUDFLARE_API_TOKEN is set (or `wrangler login` was run), the Worker's
# UPSTREAM_URL secret is updated automatically. Otherwise the script prints
# the one `wrangler secret put` command to run by hand.

$ErrorActionPreference = "Stop"

$Cloudflared = "C:\Program Files (x86)\cloudflared\cloudflared.exe"
if (-not (Test-Path -LiteralPath $Cloudflared)) {
  $Cloudflared = (Get-Command cloudflared -ErrorAction SilentlyContinue)?.Source
}
if (-not $Cloudflared) { throw "cloudflared not found. Install it: winget install Cloudflare.cloudflared" }

$Log = "$env:TEMP\uai-tunnel.log"
Remove-Item $Log -Force -ErrorAction SilentlyContinue

$proc = Start-Process $Cloudflared `
  -ArgumentList "tunnel --url http://127.0.0.1:8080 --no-autoupdate" `
  -RedirectStandardError $Log -WindowStyle Hidden -PassThru
"cloudflared pid=$($proc.Id), waiting for URL..."

$url = $null
for ($i = 0; $i -lt 30 -and -not $url; $i++) {
  Start-Sleep -Seconds 2
  $m = Select-String -Path $Log -Pattern "https://[a-z0-9-]+\.trycloudflare\.com" -AllMatches -ErrorAction SilentlyContinue |
       Select-Object -First 1
  if ($m) { $url = $m.Matches[0].Value }
}

if (-not $url) { throw "No tunnel URL appeared in $Log after 60s. Check the log." }

""
"TUNNEL LIVE: $url"
""

$wrangler = Get-Command wrangler -ErrorAction SilentlyContinue
if ($wrangler) {
  "Updating Worker secret..."
  # pipe without trailing newline issues: use --stdin via echo
  $url | & wrangler secret put UPSTREAM_URL 2>&1 | Select-Object -Last 3
  ""
  "Done. Public URL is your workers.dev address (unchanged across restarts)."
} else {
  "wrangler not found. Install it (npm i -g wrangler) and run once:"
  "  wrangler login"
  "Then after every tunnel restart, run:"
  "  echo $url | wrangler secret put UPSTREAM_URL"
}
