using System;
using System.Data;
using System.Globalization;
using Dapper;

namespace TallyAuditAssistant.Data;

public class SqliteDecimalHandler : SqlMapper.TypeHandler<decimal>
{
    public static readonly SqliteDecimalHandler Instance = new();

    public override void SetValue(IDbDataParameter parameter, decimal value)
    {
        parameter.Value = value;
    }

    public override decimal Parse(object value)
    {
        if (value == null || value is DBNull) return 0m;
        if (value is decimal d) return d;
        if (value is long l) return Convert.ToDecimal(l);
        if (value is int i) return Convert.ToDecimal(i);
        if (value is double db) return Convert.ToDecimal(db);
        if (value is float f) return Convert.ToDecimal(f);
        if (value is string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return 0m;
            if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)) return parsed;
        }

        return Convert.ToDecimal(value, CultureInfo.InvariantCulture);
    }
}

public class SqliteNullableDecimalHandler : SqlMapper.TypeHandler<decimal?>
{
    public static readonly SqliteNullableDecimalHandler Instance = new();

    public override void SetValue(IDbDataParameter parameter, decimal? value)
    {
        parameter.Value = (object?)value ?? DBNull.Value;
    }

    public override decimal? Parse(object value)
    {
        if (value == null || value is DBNull) return null;
        if (value is decimal d) return d;
        if (value is long l) return Convert.ToDecimal(l);
        if (value is int i) return Convert.ToDecimal(i);
        if (value is double db) return Convert.ToDecimal(db);
        if (value is float f) return Convert.ToDecimal(f);
        if (value is string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            if (decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)) return parsed;
        }

        return Convert.ToDecimal(value, CultureInfo.InvariantCulture);
    }
}
