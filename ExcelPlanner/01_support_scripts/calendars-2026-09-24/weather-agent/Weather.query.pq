let
    // Blank WeatherApiKey: local CSV only. API branch is evaluated only for a nonblank key.
    Headers = {"Code", "ObservedUtc", "FetchedUtc", "TemperatureC", "FeelsLikeC", "Description", "WindMs", "Status", "Source"},
    CatalogHeaders = {"Code", "City", "WindowsId", "IanaId", "Latitude", "Longitude", "CountryCode"},
    OutputType = type table [Code = text, ObservedUtc = nullable datetime, FetchedUtc = nullable datetime,
        TemperatureC = nullable number, FeelsLikeC = nullable number, Description = nullable text,
        WindMs = nullable number, Status = text, Source = text],
    Fail = (code as text) as any => error Error.Record("Weather", code, null),
    RawKey = Excel.CurrentWorkbook(){[Name="WeatherApiKey"]}[Content]{0}[Column1],
    Key = if RawKey = null then "" else if Value.Is(RawKey, type text)
          then Text.Trim(RawKey) else Fail("INVALID_KEY_FIELD"),
    FolderValue = Excel.CurrentWorkbook(){[Name="DataFolder"]}[Content]{0}[Column1],
    Folder = if not Value.Is(FolderValue, type text) then Fail("INVALID_DATA_FOLDER")
             else if Text.Trim(FolderValue) = "" then Fail("EMPTY_DATA_FOLDER") else Text.Trim(FolderValue),
    // Keep local source access in top-level steps, not a closure over workbook data.
    CsvRaw = Csv.Document(File.Contents(Text.TrimEnd(Folder, {"/", "\"}) & "/Weather.csv"), [Delimiter=";", Encoding=65001, QuoteStyle=QuoteStyle.Csv]),
    CsvHeaders = Table.PromoteHeaders(CsvRaw, [PromoteAllScalars=true]),
    CsvChecked = if Table.ColumnNames(CsvHeaders)=Headers then CsvHeaders else Fail("CSV_SCHEMA_MISMATCH"),
    CatalogRaw = Csv.Document(File.Contents(Text.TrimEnd(Folder, {"/", "\"}) & "/Cities.csv"), [Delimiter=";", Encoding=65001, QuoteStyle=QuoteStyle.Csv]),
    CatalogPromoted = Table.PromoteHeaders(CatalogRaw, [PromoteAllScalars=true]),
    CatalogChecked = if Table.ColumnNames(CatalogPromoted)=CatalogHeaders then CatalogPromoted else Fail("CSV_SCHEMA_MISMATCH"),
    SelectedRange = Excel.CurrentWorkbook(){[Name="ZoneKeys"]}[Content],
    TextCode = (value as any) as text =>
        if not Value.Is(value, type text) then Fail("INVALID_CITY_CODE")
        else if value = "" or value <> Text.Trim(value) then Fail("INVALID_CITY_CODE") else value,
    Finite = (value as any) as number =>
        if not Value.Is(value, type number) then Fail("INVALID_NUMBER")
        else if Number.IsNaN(value) or value = #infinity or value = -#infinity
        then Fail("INVALID_NUMBER") else value,
    NumberCell = (value as any) as nullable number =>
        if value = null or value = "" then null else Finite(Number.FromText(value, "en-US")),
    UtcCell = (value as any) as nullable datetime =>
        if value = null or value = "" then null
        else let parsed = DateTime.FromText(value, [Format = "yyyy-MM-dd'T'HH:mm:ss", Culture = "en-US"])
             in if DateTime.ToText(parsed, [Format = "yyyy-MM-dd'T'HH:mm:ss", Culture = "en-US"]) <> value
                then Fail("INVALID_UTC_TIMESTAMP") else parsed,
    UniqueCodes = (tableValue as table) as table =>
        let codes = List.Buffer(List.Transform(tableValue[Code], each TextCode(_)))
        in if List.Count(List.Distinct(codes)) <> List.Count(codes)
           then Fail("DUPLICATE_CITY_CODE") else tableValue,
    CsvMode = () as table =>
        let
            input = UniqueCodes(CsvChecked),
            typed = Table.TransformColumns(input, {
                {"Code", TextCode, type text}, {"ObservedUtc", UtcCell, type nullable datetime},
                {"FetchedUtc", UtcCell, type nullable datetime},
                {"TemperatureC", NumberCell, type nullable number}, {"FeelsLikeC", NumberCell, type nullable number},
                {"WindMs", NumberCell, type nullable number}}),
            rows = Table.ToRecords(typed),
            valid = List.AllTrue(List.Transform(rows, (row) =>
                Value.Is(row[Status], type text) and row[Status] <> ""
                and Value.Is(row[Source], type text) and row[Source] <> ""
                and (row[ObservedUtc] = null or Value.Is(row[ObservedUtc], type datetime))
                and (row[FetchedUtc] = null or Value.Is(row[FetchedUtc], type datetime))
                and (row[TemperatureC] = null or Value.Is(row[TemperatureC], type number))
                and (row[FeelsLikeC] = null or Value.Is(row[FeelsLikeC], type number))
                and (row[WindMs] = null or row[WindMs] >= 0))),
            result = if valid then Value.ReplaceType(Table.Buffer(typed), OutputType)
                     else Fail("INVALID_CSV_ROW")
        in result,
    ApiMode = () as table =>
        let
            selections = SelectedRange,
            selectedValues = if Table.ColumnNames(selections) = {"Column1"}
                             and Table.RowCount(selections) <= 7
                             then selections[Column1] else Fail("INVALID_ZONE_KEYS_RANGE"),
            nonblank = List.Select(selectedValues, each _ <> null and _ <> ""),
            selected = List.Buffer(List.Distinct(List.Transform(nonblank, each TextCode(_)))),
            codes = if List.Count(selected) >= 1 and List.Count(selected) <= 7
                    then selected else Fail("SELECT_ONE_TO_SEVEN_CITIES"),
            catalog = UniqueCodes(CatalogChecked),
            coordinates = List.Buffer(List.Transform(codes, (code) =>
                let
                    matches = Table.SelectRows(catalog, each [Code] = code),
                    city = if Table.RowCount(matches) = 1 then matches{0}
                           else Fail("CITY_MISSING_OR_AMBIGUOUS"),
                    lat = NumberCell(city[Latitude]), lon = NumberCell(city[Longitude]),
                    country = city[CountryCode],
                    valid = lat <> null and lon <> null and lat >= -90 and lat <= 90
                        and lon >= -180 and lon <= 180 and Value.Is(country, type text)
                        and Text.Length(country) = 2 and Text.Select(country, {"A".."Z"}) = country
                in if valid then [Code = code, Latitude = lat, Longitude = lon, CountryCode = country]
                   else Fail("MISSING_OR_INVALID_COORDINATES"))),
            // Force validation for all selected cities before any request is evaluated.
            allValid = List.AllTrue(List.Transform(coordinates, each
                [Latitude] >= -90 and [Longitude] >= -180 and Text.Length([CountryCode]) = 2)),
            Fetch = (city as record) as list =>
                let
                    attempted = try
                        let
                            raw = Web.Contents("https://api.openweathermap.org", [
                                RelativePath = "data/2.5/weather",
                                Query = [lat = Number.ToText(city[Latitude], "G", "en-US"),
                                    lon = Number.ToText(city[Longitude], "G", "en-US"),
                                    appid = Key, units = "metric", lang = "it"],
                                Timeout = #duration(0, 0, 0, 20),
                                ManualStatusHandling = {400,404,408,429,500,502,503,504,509}]),
                            status = Record.FieldOrDefault(Value.Metadata(raw), "Response.Status", 200),
                            payload = if status = 200 then Json.Document(raw) else Fail("WEATHER_API_ERROR"),
                            ok = Text.From(payload[cod], "en-US") = "200"
                                and payload[sys][country] = city[CountryCode],
                            epoch = Finite(payload[dt]),
                            observed = if epoch > 0 and Number.RoundDown(epoch) = epoch
                                       then #datetime(1970, 1, 1, 0, 0, 0) + #duration(0, 0, 0, epoch)
                                       else Fail("INVALID_OBSERVATION_TIME"),
                            fetched = DateTimeZone.RemoveZone(DateTimeZone.UtcNow()),
                            temp = Finite(payload[main][temp]), feels = Finite(payload[main][feels_like]),
                            wind = Finite(payload[wind][speed]), description = payload[weather]{0}[description],
                            descriptionOk = Value.Is(description, type text) and Text.Length(description) > 0
                                and Text.Length(description) <= 200 and not Text.Contains(description, Key),
                            values = if ok and wind >= 0 and descriptionOk
                                     and temp = Finite(temp) and feels = Finite(feels)
                                     and observed <= fetched + #duration(0, 0, 2, 0)
                                     then {city[Code], observed, fetched, temp, feels, description, wind, "api", "OpenWeather API"}
                                     else Fail("WEATHER_API_ERROR"),
                            // Force primitive values before building the outer table.
                            checked = List.Buffer(values)
                        in checked,
                    sanitized = if attempted[HasError] then Fail("WEATHER_API_ERROR") else attempted[Value]
                in sanitized,
            rows = if allValid then List.Buffer(List.Transform(coordinates, each Fetch(_))) else Fail("MISSING_OR_INVALID_COORDINATES"),
            tableValue = Table.FromRows(rows, Headers),
            result = Table.TransformColumnTypes(tableValue, {{"Code",type text},{"ObservedUtc",type datetime},{"FetchedUtc",type datetime},{"TemperatureC",type number},{"FeelsLikeC",type number},{"Description",type text},{"WindMs",type number},{"Status",type text},{"Source",type text}}, "en-US"),
            // Excel can display row-level query errors as blank rows; reject the refresh instead.
            verified = if Table.RowCount(Table.SelectRowsWithErrors(result))>0 then Fail("WEATHER_API_ERROR")
                       else if List.NonNullCount(result[Code])<>List.Count(codes) then Fail("INCOMPLETE_WEATHER_RESULT") else result
        in verified,
    // No raw HTTP exceptions, request URLs, binary metadata or key fields reach output.
    Attempt = try Table.Buffer(if Key = "" then CsvMode() else ApiMode()),
    Result = if Attempt[HasError] then Fail("WEATHER_REFRESH_FAILED") else Attempt[Value]
in
    Result
