using Microsoft.Data.Sqlite;

namespace RemoteNest.Data;

/// <summary>
/// Owns the SQLite connection string and creates the schema on first run.
/// Column names match <c>ConnectionProfile</c> property names exactly — the CRUD SQL in
/// <c>ConnectionService</c> is generated from that property list, so the two must stay
/// in step (a test asserts the table matches the model).
/// </summary>
public sealed class Database
{
    private const string SchemaSql = """
        CREATE TABLE IF NOT EXISTS "ConnectionProfiles" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_ConnectionProfiles" PRIMARY KEY AUTOINCREMENT,
            "Name" TEXT NOT NULL,
            "Group" TEXT NOT NULL,
            "Host" TEXT NOT NULL,
            "Port" INTEGER NOT NULL DEFAULT 3389,
            "Username" TEXT NOT NULL DEFAULT '',
            "EncryptedPassword" TEXT NOT NULL DEFAULT '',
            "Domain" TEXT NOT NULL DEFAULT '',
            "ScreenWidth" INTEGER NOT NULL DEFAULT 1920,
            "ScreenHeight" INTEGER NOT NULL DEFAULT 1080,
            "FullScreen" INTEGER NOT NULL DEFAULT 0,
            "ColorDepth" TEXT NOT NULL DEFAULT '32',
            "UseMultimon" INTEGER NOT NULL DEFAULT 0,
            "SelectedMonitors" TEXT NOT NULL DEFAULT '',
            "DynamicResolution" INTEGER NOT NULL DEFAULT 1,
            "SmartSizing" INTEGER NOT NULL DEFAULT 0,
            "DesktopScaleFactor" INTEGER NOT NULL DEFAULT 0,
            "RedirectClipboard" INTEGER NOT NULL DEFAULT 1,
            "RedirectDrives" INTEGER NOT NULL DEFAULT 0,
            "DrivesToRedirect" TEXT NOT NULL DEFAULT '',
            "RedirectPrinters" INTEGER NOT NULL DEFAULT 0,
            "AudioPlaybackMode" INTEGER NOT NULL DEFAULT 0,
            "AudioCaptureMode" INTEGER NOT NULL DEFAULT 0,
            "AudioQualityMode" INTEGER NOT NULL DEFAULT 0,
            "CamerasToRedirect" TEXT NOT NULL DEFAULT '',
            "PnpDevicesToRedirect" TEXT NOT NULL DEFAULT '',
            "UsbDevicesToRedirect" TEXT NOT NULL DEFAULT '',
            "RedirectComPorts" INTEGER NOT NULL DEFAULT 0,
            "RedirectSmartCards" INTEGER NOT NULL DEFAULT 1,
            "RedirectWebAuthn" INTEGER NOT NULL DEFAULT 1,
            "RedirectLocation" INTEGER NOT NULL DEFAULT 0,
            "KeyboardHook" INTEGER NOT NULL DEFAULT 2,
            "UseNetworkLevelAuth" INTEGER NOT NULL DEFAULT 1,
            "GatewayHostname" TEXT NOT NULL DEFAULT '',
            "GatewayUsageMethod" INTEGER NOT NULL DEFAULT 2,
            "GatewayCredentialsSource" INTEGER NOT NULL DEFAULT 0,
            "GatewayPromptCredentialOnce" INTEGER NOT NULL DEFAULT 1,
            "LoadBalanceInfo" TEXT NOT NULL DEFAULT '',
            "Pcb" TEXT NOT NULL DEFAULT '',
            "KdcProxyName" TEXT NOT NULL DEFAULT '',
            "EnableRdsAadAuth" INTEGER NOT NULL DEFAULT 0,
            "ConnectionType" INTEGER NOT NULL DEFAULT 7,
            "NetworkAutoDetect" INTEGER NOT NULL DEFAULT 1,
            "BandwidthAutoDetect" INTEGER NOT NULL DEFAULT 1,
            "Compression" INTEGER NOT NULL DEFAULT 1,
            "BitmapCachePersist" INTEGER NOT NULL DEFAULT 1,
            "DisableWallpaper" INTEGER NOT NULL DEFAULT 0,
            "DisableFullWindowDrag" INTEGER NOT NULL DEFAULT 0,
            "DisableMenuAnims" INTEGER NOT NULL DEFAULT 0,
            "DisableThemes" INTEGER NOT NULL DEFAULT 0,
            "AllowFontSmoothing" INTEGER NOT NULL DEFAULT 0,
            "AllowDesktopComposition" INTEGER NOT NULL DEFAULT 0,
            "VideoPlaybackMode" INTEGER NOT NULL DEFAULT 1,
            "AdministrativeSession" INTEGER NOT NULL DEFAULT 0,
            "AutoReconnect" INTEGER NOT NULL DEFAULT 1,
            "AutoReconnectMaxRetries" INTEGER NOT NULL DEFAULT 20,
            "DisplayConnectionBar" INTEGER NOT NULL DEFAULT 1,
            "PinConnectionBar" INTEGER NOT NULL DEFAULT 1,
            "PublicMode" INTEGER NOT NULL DEFAULT 0,
            "AuthenticationLevel" INTEGER NOT NULL DEFAULT 2,
            "RestrictedAdmin" INTEGER NOT NULL DEFAULT 0,
            "RemoteGuard" INTEGER NOT NULL DEFAULT 0,
            "PromptForCredentials" INTEGER NOT NULL DEFAULT 0,
            "RemoteAppMode" INTEGER NOT NULL DEFAULT 0,
            "RemoteAppProgram" TEXT NOT NULL DEFAULT '',
            "RemoteAppName" TEXT NOT NULL DEFAULT '',
            "RemoteAppCmdLine" TEXT NOT NULL DEFAULT '',
            "ExtraSettings" TEXT NOT NULL DEFAULT '',
            "AutoConnectOnStartup" INTEGER NOT NULL DEFAULT 0,
            "Notes" TEXT NOT NULL DEFAULT '',
            "CreatedAt" TEXT NOT NULL,
            "LastConnectedAt" TEXT NOT NULL,
            "ConnectionCount" INTEGER NOT NULL DEFAULT 0
        );
        CREATE INDEX IF NOT EXISTS "IX_ConnectionProfiles_Group" ON "ConnectionProfiles" ("Group");
        CREATE INDEX IF NOT EXISTS "IX_ConnectionProfiles_Host" ON "ConnectionProfiles" ("Host");
        CREATE INDEX IF NOT EXISTS "IX_ConnectionProfiles_Name" ON "ConnectionProfiles" ("Name");
        """;

    private string ConnectionString { get; }

    public Database(string dbPath)
    {
        ConnectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
    }

    public SqliteConnection Open()
    {
        var conn = new SqliteConnection(ConnectionString);
        conn.Open();
        return conn;
    }

    /// <summary>
    /// Creates the schema when missing. Runs in a few milliseconds — safe to call
    /// synchronously during startup.
    /// </summary>
    public void EnsureCreated()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = SchemaSql;
        cmd.ExecuteNonQuery();
    }
}
