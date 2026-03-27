using System.Data;
using Dapper;

namespace MasterPlan.SyncEngine.Models;

/// <summary>
/// Dapper type handler for DateTime values that ensures SQL Server compatibility.
/// SQL Server's datetime range is 1/1/1753 to 12/31/9999.
/// This handler converts out-of-range dates (like DateTime.MinValue) to NULL.
/// </summary>
public class SqlDateTimeHandler : SqlMapper.TypeHandler<DateTime>
{
    // SQL Server minimum date for datetime type
    private static readonly DateTime SqlMinDate = new(1753, 1, 1);

    public override DateTime Parse(object value)
    {
        return (DateTime)value;
    }

    public override void SetValue(IDbDataParameter parameter, DateTime value)
    {
        // Convert out-of-range dates to NULL
        if (value < SqlMinDate)
        {
            parameter.Value = DBNull.Value;
        }
        else
        {
            parameter.Value = value;
        }
    }
}

/// <summary>
/// Dapper type handler for nullable DateTime values that ensures SQL Server compatibility.
/// SQL Server's datetime range is 1/1/1753 to 12/31/9999.
/// This handler converts out-of-range dates (like DateTime.MinValue) to NULL.
/// </summary>
public class SqlNullableDateTimeHandler : SqlMapper.TypeHandler<DateTime?>
{
    // SQL Server minimum date for datetime type
    private static readonly DateTime SqlMinDate = new(1753, 1, 1);

    public override DateTime? Parse(object value)
    {
        if (value == null || value == DBNull.Value)
            return null;
        return (DateTime)value;
    }

    public override void SetValue(IDbDataParameter parameter, DateTime? value)
    {
        // Convert null or out-of-range dates to NULL
        if (!value.HasValue || value.Value < SqlMinDate)
        {
            parameter.Value = DBNull.Value;
        }
        else
        {
            parameter.Value = value.Value;
        }
    }
}

/// <summary>
/// Dapper type handler for TimeSpan values that properly maps to SQL Server TIME type.
/// Without this handler, Dapper/SqlClient incorrectly tries to convert TimeSpan to datetime,
/// causing "SqlDateTime overflow" errors.
/// </summary>
public class SqlTimeSpanHandler : SqlMapper.TypeHandler<TimeSpan>
{
    public override TimeSpan Parse(object value)
    {
        return (TimeSpan)value;
    }

    public override void SetValue(IDbDataParameter parameter, TimeSpan value)
    {
        parameter.DbType = DbType.Time;
        parameter.Value = value;
    }
}

/// <summary>
/// Dapper type handler for nullable TimeSpan values that properly maps to SQL Server TIME type.
/// Without this handler, Dapper/SqlClient incorrectly tries to convert TimeSpan to datetime,
/// causing "SqlDateTime overflow" errors.
/// </summary>
public class SqlNullableTimeSpanHandler : SqlMapper.TypeHandler<TimeSpan?>
{
    public override TimeSpan? Parse(object value)
    {
        if (value == null || value == DBNull.Value)
            return null;
        return (TimeSpan)value;
    }

    public override void SetValue(IDbDataParameter parameter, TimeSpan? value)
    {
        parameter.DbType = DbType.Time;
        if (value.HasValue)
        {
            parameter.Value = value.Value;
        }
        else
        {
            parameter.Value = DBNull.Value;
        }
    }
}
