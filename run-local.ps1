# Run U-AI locally for tunnelled hosting. 4 threads: this box has 4 cores.
# DATABASE_URL is intentionally unset — the app falls back to its embedded
# encrypted Neon connection string (verified working).
$env:INVITE_CODE = "UAI-6279-S3CA"

$env:ASPNETCORE_URLS = "http://127.0.0.1:8080"
$env:Model__Threads  = "4"
$env:Model__Path     = "G:\AI\online\model\model_int4"
Remove-Item Env:\DATABASE_URL -ErrorAction SilentlyContinue

dotnet run --project "G:\AI\online\U-AI\UAI.csproj" -c Release
