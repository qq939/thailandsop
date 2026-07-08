using System.Globalization;
using SqlSugar;

namespace VideoInferenceDemo;

public static class ResultDbSession
{
    private static readonly object Gate = new();
    private static string _resultsDirectory = string.Empty;
    private static Func<DateTimeOffset> _nowProvider = () => DateTimeOffset.Now;
    private static SqlSugarScope? _scope;
    private static DateOnly? _activeDate;
    private static string _activePath = string.Empty;
    private static readonly List<SqlSugarScope> RetiredScopes = [];

    public static event EventHandler? DatabaseChanged;

    public static bool IsInitialized { get; private set; }

    public static string ResultsDirectory
    {
        get
        {
            if (!IsInitialized)
                throw new InvalidOperationException("ResultDbSession is not initialized.");
            return _resultsDirectory;
        }
    }

    public static SqlSugarScope Db
    {
        get
        {
            if (!IsInitialized)
                throw new InvalidOperationException("ResultDbSession is not initialized.");
            return GetDbForDate(DateOnly.FromDateTime(_nowProvider().LocalDateTime.Date));
        }
    }

    public static string CurrentDbPath
    {
        get
        {
            _ = Db;
            lock (Gate)
            {
                return _activePath;
            }
        }
    }

    public static void Initialize(string resultsDirectory, Func<DateTimeOffset>? nowProvider = null)
    {
        lock (Gate)
        {
            if (IsInitialized)
                return;

            _resultsDirectory = string.IsNullOrWhiteSpace(resultsDirectory)
                ? Path.Combine(AppContext.BaseDirectory, "results")
                : Path.GetFullPath(resultsDirectory.Trim());
            Directory.CreateDirectory(_resultsDirectory);
            _nowProvider = nowProvider ?? (() => DateTimeOffset.Now);
            IsInitialized = true;
        }
    }

    public static SqlSugarScope GetDbForDate(DateOnly date)
    {
        if (!IsInitialized)
            throw new InvalidOperationException("ResultDbSession is not initialized.");

        SqlSugarScope scope;
        lock (Gate)
        {
            if (_scope != null && _activeDate == date)
            {
                return _scope;
            }

            if (_scope != null)
            {
                RetiredScopes.Add(_scope);
            }

            _activeDate = date;
            _activePath = GetDbPathForDate(date);
            _scope = DbSession.CreateScope(_activePath, SqliteResultSchema.Ensure, foreignKeys: true);
            scope = _scope;
        }

        DatabaseChanged?.Invoke(null, EventArgs.Empty);
        return scope;
    }

    public static IReadOnlyList<ResultDatabaseInfo> ListDatabases(DateOnly fromInclusive, DateOnly toInclusive)
    {
        if (!IsInitialized || !Directory.Exists(_resultsDirectory) || toInclusive < fromInclusive)
            return Array.Empty<ResultDatabaseInfo>();

        var list = new List<ResultDatabaseInfo>();
        foreach (var file in Directory.EnumerateFiles(_resultsDirectory, "*.db", SearchOption.TopDirectoryOnly))
        {
            var stem = Path.GetFileNameWithoutExtension(file);
            if (!DateOnly.TryParseExact(stem, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                continue;
            if (date < fromInclusive || date > toInclusive)
                continue;

            list.Add(new ResultDatabaseInfo(date, file));
        }

        return list.OrderBy(item => item.Date).ToList();
    }

    public static SqlSugarScope OpenScopeForDate(DateOnly date)
    {
        if (!IsInitialized)
            throw new InvalidOperationException("ResultDbSession is not initialized.");

        return DbSession.CreateScope(GetDbPathForDate(date), SqliteResultSchema.Ensure, foreignKeys: true);
    }

    public static string GetDbPathForDate(DateOnly date)
    {
        if (!IsInitialized)
            throw new InvalidOperationException("ResultDbSession is not initialized.");
        return Path.Combine(_resultsDirectory, $"{date:yyyy-MM-dd}.db");
    }

    internal static void Reset()
    {
        lock (Gate)
        {
            _scope?.Dispose();
            foreach (var scope in RetiredScopes)
            {
                scope.Dispose();
            }

            RetiredScopes.Clear();
            _scope = null;
            _activeDate = null;
            _activePath = string.Empty;
            _resultsDirectory = string.Empty;
            _nowProvider = () => DateTimeOffset.Now;
            IsInitialized = false;
            DatabaseChanged = null;
        }
    }
}

public sealed record ResultDatabaseInfo(DateOnly Date, string Path);
