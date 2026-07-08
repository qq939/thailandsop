using SqlSugar;

namespace VideoInferenceDemo;

public static class SqliteSchemaV4
{
    public static void Ensure(ISqlSugarClient db)
    {
        SqliteConfigSchema.Ensure(db);
        SqliteResultSchema.Ensure(db);
    }

    public static void EnsureViews(ISqlSugarClient db)
    {
        SqliteResultSchema.EnsureViews(db);
    }
}
