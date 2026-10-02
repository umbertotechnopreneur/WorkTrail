// SPDX-License-Identifier: MIT

using System.Globalization;
using Microsoft.Data.Sqlite;
using WorkTrail.Application;

namespace WorkTrail.Services;

/// <summary>Stores the embedded celestial calendar in normalized tables of the existing activity database.</summary>
internal sealed partial class SqliteActivityStore
{
    private const string CalendarSchemaSql = """
        CREATE TABLE calendar_dataset (
            id INTEGER PRIMARY KEY CHECK (id = 1),
            sha256 TEXT NOT NULL,
            coverage_start TEXT NOT NULL,
            coverage_end TEXT NOT NULL,
            holiday_library TEXT NOT NULL,
            saints_revision TEXT NOT NULL
        );
        CREATE TABLE calendar_countries (
            code TEXT NOT NULL PRIMARY KEY,
            name TEXT NOT NULL,
            scope TEXT NOT NULL,
            source_url TEXT NOT NULL
        );
        CREATE TABLE calendar_holidays (
            country_code TEXT NOT NULL REFERENCES calendar_countries(code),
            date TEXT NOT NULL,
            name TEXT NOT NULL,
            kind TEXT NOT NULL CHECK (kind IN ('holiday', 'workday')),
            quality TEXT NOT NULL CHECK (quality IN ('rule_based', 'estimated', 'provisional')),
            artwork_file_name TEXT NOT NULL,
            source_url TEXT NOT NULL,
            PRIMARY KEY (country_code, date, kind, name)
        );
        CREATE INDEX ix_calendar_holidays_date ON calendar_holidays (date, country_code);
        CREATE TABLE calendar_saints (
            event_key TEXT NOT NULL PRIMARY KEY,
            month INTEGER NOT NULL CHECK (month BETWEEN 1 AND 12),
            day INTEGER NOT NULL CHECK (day BETWEEN 1 AND 31),
            name_latin TEXT NOT NULL,
            source_url TEXT NOT NULL
        );
        CREATE INDEX ix_calendar_saints_month_day ON calendar_saints (month, day);
        """;

    private static void ValidateCalendarSchema(SqliteConnection connection)
    {
        foreach (var table in new[] { "calendar_dataset", "calendar_countries", "calendar_holidays", "calendar_saints" })
        {
            ValidateCreateStatement(connection, table, CalendarSchemaSql);
        }

        if (ReadIndexes(connection, "calendar_dataset").Count != 0
            || !ReadIndexes(connection, "calendar_countries").SetEquals(["sqlite_autoindex_calendar_countries_1"])
            || !ReadIndexes(connection, "calendar_holidays").SetEquals(
                ["sqlite_autoindex_calendar_holidays_1", "ix_calendar_holidays_date"])
            || !ReadIndexes(connection, "calendar_saints").SetEquals(
                ["sqlite_autoindex_calendar_saints_1", "ix_calendar_saints_month_day"]))
        {
            throw new InvalidOperationException("The celestial calendar indexes do not match the supported schema.");
        }
    }

    /// <summary>Replaces a changed bundled catalog in one transaction; identical content causes no database write.</summary>
    internal void EnsureCelestialCalendar(CelestialCalendarDataset dataset)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        using var connection = OpenConnection();
        using var read = connection.CreateCommand();
        read.CommandText = "SELECT sha256 FROM calendar_dataset WHERE id = 1;";
        if (string.Equals(read.ExecuteScalar() as string, dataset.Sha256, StringComparison.Ordinal))
        {
            return;
        }

        using var transaction = connection.BeginTransaction();
        using (var clear = connection.CreateCommand())
        {
            clear.Transaction = transaction;
            clear.CommandText = "DELETE FROM calendar_holidays; DELETE FROM calendar_saints; "
                + "DELETE FROM calendar_countries; DELETE FROM calendar_dataset;";
            clear.ExecuteNonQuery();
        }

        foreach (var country in dataset.Countries)
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO calendar_countries (code, name, scope, source_url) "
                + "VALUES ($code, $name, $scope, $source);";
            Add(insert, "$code", country.Code);
            Add(insert, "$name", country.Name);
            Add(insert, "$scope", country.Scope);
            Add(insert, "$source", country.SourceUrl);
            insert.ExecuteNonQuery();
        }

        foreach (var holiday in dataset.Holidays)
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO calendar_holidays "
                + "(country_code, date, name, kind, quality, artwork_file_name, source_url) "
                + "VALUES ($country, $date, $name, $kind, $quality, $artwork, $source);";
            Add(insert, "$country", holiday.Country);
            Add(insert, "$date", holiday.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            Add(insert, "$name", holiday.Name);
            Add(insert, "$kind", holiday.Kind);
            Add(insert, "$quality", holiday.Quality);
            Add(insert, "$artwork", holiday.ArtworkFileName);
            Add(insert, "$source", holiday.SourceUrl);
            insert.ExecuteNonQuery();
        }

        foreach (var saint in dataset.Saints)
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO calendar_saints (event_key, month, day, name_latin, source_url) "
                + "VALUES ($key, $month, $day, $name, $source);";
            Add(insert, "$key", saint.EventKey);
            Add(insert, "$month", saint.Month);
            Add(insert, "$day", saint.Day);
            Add(insert, "$name", saint.NameLatin);
            Add(insert, "$source", saint.SourceUrl);
            insert.ExecuteNonQuery();
        }

        using (var insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO calendar_dataset "
                + "(id, sha256, coverage_start, coverage_end, holiday_library, saints_revision) "
                + "VALUES (1, $sha, $start, $end, $holidays, $saints);";
            Add(insert, "$sha", dataset.Sha256);
            Add(insert, "$start", dataset.CoverageStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            Add(insert, "$end", dataset.CoverageEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            Add(insert, "$holidays", dataset.HolidayLibrary);
            Add(insert, "$saints", dataset.SaintsRevision);
            insert.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    /// <summary>Reads national entries in a bounded date range from the shared SQLite database.</summary>
    internal IReadOnlyList<CelestialCalendarHoliday> LoadCalendarHolidays(DateOnly start, DateOnly end)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT date, country_code, name, kind, quality, artwork_file_name, source_url "
            + "FROM calendar_holidays WHERE date BETWEEN $start AND $end ORDER BY date, country_code, kind, name;";
        Add(command, "$start", start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add(command, "$end", end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        using var reader = command.ExecuteReader();
        var rows = new List<CelestialCalendarHoliday>();
        while (reader.Read())
        {
            rows.Add(new CelestialCalendarHoliday(
                DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4),
                reader.GetString(5), reader.GetString(6)));
        }

        return rows;
    }

    /// <summary>Reads the Latin entries assigned to one recurring calendar date.</summary>
    internal IReadOnlyList<CelestialCalendarSaint> LoadCalendarSaints(DateOnly date)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT month, day, event_key, name_latin, source_url "
            + "FROM calendar_saints WHERE month = $month AND day = $day ORDER BY event_key;";
        Add(command, "$month", date.Month);
        Add(command, "$day", date.Day);
        using var reader = command.ExecuteReader();
        var rows = new List<CelestialCalendarSaint>();
        while (reader.Read())
        {
            rows.Add(new CelestialCalendarSaint(reader.GetInt32(0), reader.GetInt32(1),
                reader.GetString(2), reader.GetString(3), reader.GetString(4)));
        }

        return rows;
    }
}
