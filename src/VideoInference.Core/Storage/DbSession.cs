using SqlSugar;

namespace VideoInferenceDemo;

public static class DbSession
{
    private static SqlSugarScope? _scope;
    private static SqlSugarScope? _configScope;
    private static bool _initialized;
    private static readonly object _gate = new();

    public static SqlSugarScope Db
    {
        get
        {
            if (!_initialized)
                throw new InvalidOperationException("DbSession is not initialized. Call DbSession.Initialize(dbPath) first.");
            return _scope!;
        }
    }

    public static SqlSugarScope ConfigDb
    {
        get
        {
            if (!_initialized)
                throw new InvalidOperationException("DbSession is not initialized. Call DbSession.Initialize(dbPath) first.");
            return _configScope ?? _scope!;
        }
    }

    public static SqlSugarScope ResultDb => ResultDbSession.IsInitialized ? ResultDbSession.Db : Db;

    public static void Initialize(string dbPath)
    {
        if (_initialized) return;
        lock (_gate)
        {
            if (_initialized) return;

            _scope = CreateScope(dbPath, SqliteSchemaV4.Ensure, foreignKeys: true);
            _initialized = true;
        }
    }

    public static void InitializeSplit(string configDbPath, string resultsDirectory)
    {
        if (_initialized) return;
        lock (_gate)
        {
            if (_initialized) return;

            _configScope = CreateScope(configDbPath, SqliteConfigSchema.Ensure, foreignKeys: true);
            _scope = _configScope;
            ResultDbSession.Initialize(resultsDirectory);
            _ = ResultDbSession.Db;
            _initialized = true;
        }
    }

    public static bool IsInitialized => _initialized;

    internal static SqlSugarScope CreateScope(string dbPath, Action<ISqlSugarClient> ensureSchema, bool foreignKeys)
    {
        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        return new SqlSugarScope(new ConnectionConfig
        {
            DbType = DbType.Sqlite,
            ConnectionString = $"Data Source={dbPath};Cache=Shared;Default Timeout=5",
            IsAutoCloseConnection = true,
        }, db =>
        {
            db.Ado.ExecuteCommand("PRAGMA journal_mode=WAL;");
            db.Ado.ExecuteCommand("PRAGMA synchronous=NORMAL;");
            db.Ado.ExecuteCommand("PRAGMA temp_store=MEMORY;");
            db.Ado.ExecuteCommand(foreignKeys ? "PRAGMA foreign_keys=ON;" : "PRAGMA foreign_keys=OFF;");
            ensureSchema(db);
        });
    }

    internal static void Reset()
    {
        lock (_gate)
        {
            ResultDbSession.Reset();
            var primaryScope = _scope;
            var configScope = _configScope;
            if (!ReferenceEquals(primaryScope, configScope))
            {
                primaryScope?.Dispose();
            }

            configScope?.Dispose();
            _scope = null;
            _configScope = null;
            _initialized = false;
        }
    }
}
