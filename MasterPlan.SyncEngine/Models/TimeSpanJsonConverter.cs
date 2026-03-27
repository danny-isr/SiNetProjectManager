using System.Text.Json;
using System.Text.Json.Serialization;

namespace MasterPlan.SyncEngine.Models;

/// <summary>
/// JSON converter for TimeSpan? that handles "HH:mm" and "HH:mm:ss" string formats.
/// Used for API fields like TotalHours, StartTime, EndTime that return time strings.
/// 
/// Supported input formats:
/// - "HH:mm" (e.g., "01:15", "14:30")
/// - "HH:mm:ss" (e.g., "01:15:00", "14:30:45")
/// - "HH:mm:ss.fffffff" (e.g., "01:15:00.1234567")
/// - null or empty string → null
/// 
/// Examples:
/// - "01:15" → TimeSpan(1, 15, 0) = 75 minutes
/// - "14:30" → TimeSpan(14, 30, 0) = 14 hours 30 minutes
/// </summary>
public class TimeSpanHHmmConverter : JsonConverter<TimeSpan?>
{
    public override TimeSpan? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            var value = reader.GetString();
            
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            // Try parsing as TimeSpan directly (handles "HH:mm:ss" and "HH:mm:ss.fffffff")
            if (TimeSpan.TryParse(value, out var result))
            {
                return result;
            }

            // Handle "HH:mm" format (without seconds)
            var parts = value.Split(':');
            if (parts.Length == 2 && 
                int.TryParse(parts[0], out var hours) && 
                int.TryParse(parts[1], out var minutes))
            {
                return new TimeSpan(hours, minutes, 0);
            }

            // Handle "HH:mm:ss" with potential leading/trailing whitespace
            if (parts.Length == 3 &&
                int.TryParse(parts[0].Trim(), out hours) &&
                int.TryParse(parts[1].Trim(), out minutes) &&
                int.TryParse(parts[2].Trim(), out var seconds))
            {
                return new TimeSpan(hours, minutes, seconds);
            }

            // Could not parse - return null rather than throwing
            return null;
        }

        // Handle numeric values (assume minutes or ticks)
        if (reader.TokenType == JsonTokenType.Number)
        {
            if (reader.TryGetInt64(out var ticks))
            {
                // Assume small numbers are minutes, large numbers are ticks
                if (ticks < 10000)
                {
                    return TimeSpan.FromMinutes(ticks);
                }
                return TimeSpan.FromTicks(ticks);
            }
        }

        return null;
    }

    public override void Write(Utf8JsonWriter writer, TimeSpan? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
        {
            // Output as "HH:mm" format for API compatibility
            var ts = value.Value;
            var totalHours = (int)ts.TotalHours;
            var minutes = ts.Minutes;
            writer.WriteStringValue($"{totalHours:00}:{minutes:00}");
        }
        else
        {
            writer.WriteNullValue();
        }
    }
}

/// <summary>
/// Non-nullable version of TimeSpanHHmmConverter for TimeSpan fields.
/// </summary>
public class TimeSpanHHmmNonNullableConverter : JsonConverter<TimeSpan>
{
    public override TimeSpan Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return TimeSpan.Zero;
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            var value = reader.GetString();
            
            if (string.IsNullOrWhiteSpace(value))
            {
                return TimeSpan.Zero;
            }

            // Try parsing as TimeSpan directly
            if (TimeSpan.TryParse(value, out var result))
            {
                return result;
            }

            // Handle "HH:mm" format
            var parts = value.Split(':');
            if (parts.Length == 2 && 
                int.TryParse(parts[0], out var hours) && 
                int.TryParse(parts[1], out var minutes))
            {
                return new TimeSpan(hours, minutes, 0);
            }

            // Handle "HH:mm:ss"
            if (parts.Length == 3 &&
                int.TryParse(parts[0].Trim(), out hours) &&
                int.TryParse(parts[1].Trim(), out minutes) &&
                int.TryParse(parts[2].Trim(), out var seconds))
            {
                return new TimeSpan(hours, minutes, seconds);
            }

            return TimeSpan.Zero;
        }

        if (reader.TokenType == JsonTokenType.Number)
        {
            if (reader.TryGetInt64(out var ticks))
            {
                if (ticks < 10000)
                {
                    return TimeSpan.FromMinutes(ticks);
                }
                return TimeSpan.FromTicks(ticks);
            }
        }

        return TimeSpan.Zero;
    }

    public override void Write(Utf8JsonWriter writer, TimeSpan value, JsonSerializerOptions options)
    {
        var totalHours = (int)value.TotalHours;
        var minutes = value.Minutes;
        writer.WriteStringValue($"{totalHours:00}:{minutes:00}");
    }
}
