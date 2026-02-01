// RevitAI - AI-powered assistant for Autodesk Revit
// Copyright (C) 2025 Bryan McDonald
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System.Text.Json;
using System.Text.Json.Serialization;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;

namespace RevitAI.Tools.ReadTools;

/// <summary>
/// Tool that returns room information with optional room_id or level filter.
/// </summary>
public sealed class GetRoomInfoTool : IRevitTool
{
    private const int MaxRooms = 100;

    private static readonly JsonElement _inputSchema;
    private static readonly JsonSerializerOptions _jsonOptions;

    static GetRoomInfoTool()
    {
        var schemaJson = """
            {
                "type": "object",
                "properties": {
                    "room_id": {
                        "type": "integer",
                        "description": "Optional specific room ID to get info for"
                    },
                    "level": {
                        "type": "string",
                        "description": "Optional level name to filter rooms (e.g., 'Level 1')"
                    }
                },
                "additionalProperties": false
            }
            """;
        _inputSchema = JsonDocument.Parse(schemaJson).RootElement.Clone();

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }

    public string Name => "get_room_info";

    public string Description => "Returns room information including name, number, level, area, volume, and whether the room is properly enclosed. Can filter by specific room ID or level name.";

    public JsonElement InputSchema => _inputSchema;

    public bool RequiresTransaction => false;

    public Task<ToolResult> ExecuteAsync(JsonElement input, UIApplication app, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var doc = app.ActiveUIDocument?.Document;
        if (doc == null)
            return Task.FromResult(ToolResult.Error("No active document. Please open a Revit project first."));

        try
        {
            // Check for specific room ID
            long? specificRoomId = null;
            if (input.TryGetProperty("room_id", out var roomIdElement))
            {
                if (roomIdElement.ValueKind == JsonValueKind.Number)
                    specificRoomId = roomIdElement.GetInt64();
                else if (roomIdElement.ValueKind == JsonValueKind.String && long.TryParse(roomIdElement.GetString(), out var parsed))
                    specificRoomId = parsed;
            }

            // If specific room ID provided, return just that room
            if (specificRoomId.HasValue)
            {
                var element = doc.GetElement(new ElementId(specificRoomId.Value));
                if (element is not Room room)
                    return Task.FromResult(ToolResult.Error($"Element {specificRoomId.Value} is not a room or was not found."));

                var roomData = ExtractRoomData(room, doc);
                var singleResult = new GetRoomInfoResult
                {
                    Rooms = new List<RoomData> { roomData },
                    Count = 1,
                    Truncated = false
                };
                return Task.FromResult(ToolResult.Ok(JsonSerializer.Serialize(singleResult, _jsonOptions)));
            }

            // Get optional level filter
            string? levelFilter = null;
            Level? filterLevel = null;
            if (input.TryGetProperty("level", out var levelElement))
            {
                levelFilter = levelElement.GetString();
                if (!string.IsNullOrWhiteSpace(levelFilter))
                {
                    filterLevel = FindLevelByName(doc, levelFilter);
                    if (filterLevel == null)
                    {
                        var availableLevels = GetAvailableLevelNames(doc);
                        return Task.FromResult(ToolResult.Error(
                            $"Level '{levelFilter}' not found. Available levels: {string.Join(", ", availableLevels)}"));
                    }
                }
            }

            // Collect all rooms
            var collector = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Rooms)
                .WhereElementIsNotElementType();

            var allRooms = collector
                .Cast<Room>()
                .Where(r => r.Area > 0 || !IsPlacedRoom(r)) // Include rooms even if not enclosed
                .ToList();

            // Apply level filter
            if (filterLevel != null)
            {
                allRooms = allRooms.Where(r => r.LevelId == filterLevel.Id).ToList();
            }

            var totalCount = allRooms.Count;
            var truncated = totalCount > MaxRooms;

            var rooms = allRooms
                .Take(MaxRooms)
                .Select(r => ExtractRoomData(r, doc))
                .OrderBy(r => r.Level)
                .ThenBy(r => r.Number)
                .ThenBy(r => r.Name)
                .ToList();

            // Summary stats
            var totalArea = rooms.Where(r => r.IsEnclosed).Sum(r => r.Area ?? 0);
            var enclosedCount = rooms.Count(r => r.IsEnclosed);
            var notEnclosedCount = rooms.Count(r => !r.IsEnclosed);

            var result = new GetRoomInfoResult
            {
                Rooms = rooms,
                Count = totalCount,
                LevelFilter = filterLevel?.Name,
                Truncated = truncated,
                TruncatedMessage = truncated ? $"Showing {MaxRooms} of {totalCount} rooms." : null,
                Summary = new RoomSummary
                {
                    TotalArea = Math.Round(totalArea, 2),
                    EnclosedRooms = enclosedCount,
                    NotEnclosedRooms = notEnclosedCount
                }
            };

            return Task.FromResult(ToolResult.Ok(JsonSerializer.Serialize(result, _jsonOptions)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolResult.Error($"Failed to get room info: {ex.Message}"));
        }
    }

    private static bool IsPlacedRoom(Room room)
    {
        try
        {
            var location = room.Location;
            return location != null;
        }
        catch
        {
            return false;
        }
    }

    private static Level? FindLevelByName(Document doc, string name)
    {
        return new FilteredElementCollector(doc)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .FirstOrDefault(l => l.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    private static List<string> GetAvailableLevelNames(Document doc)
    {
        return new FilteredElementCollector(doc)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .OrderBy(l => l.Elevation)
            .Select(l => l.Name)
            .ToList();
    }

    private static RoomData ExtractRoomData(Room room, Document doc)
    {
        var data = new RoomData
        {
            Id = room.Id.Value,
            Name = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? room.Name,
            Number = room.Number
        };

        // Get level
        if (room.LevelId != ElementId.InvalidElementId)
        {
            var level = doc.GetElement(room.LevelId) as Level;
            data.Level = level?.Name;
            data.LevelId = level?.Id.Value;
        }

        // Area and volume
        data.Area = room.Area > 0 ? Math.Round(room.Area, 2) : null;
        data.Volume = room.Volume > 0 ? Math.Round(room.Volume, 2) : null;

        // Formatted values
        var areaParam = room.get_Parameter(BuiltInParameter.ROOM_AREA);
        if (areaParam != null && areaParam.HasValue)
            data.AreaFormatted = areaParam.AsValueString();

        var volumeParam = room.get_Parameter(BuiltInParameter.ROOM_VOLUME);
        if (volumeParam != null && volumeParam.HasValue)
            data.VolumeFormatted = volumeParam.AsValueString();

        // Check if enclosed
        data.IsEnclosed = room.Area > 0;

        // Get perimeter
        var perimeterParam = room.get_Parameter(BuiltInParameter.ROOM_PERIMETER);
        if (perimeterParam != null && perimeterParam.HasValue)
        {
            data.Perimeter = Math.Round(perimeterParam.AsDouble(), 2);
            data.PerimeterFormatted = perimeterParam.AsValueString();
        }

        // Get location point
        if (room.Location is LocationPoint locationPoint)
        {
            var pt = locationPoint.Point;
            data.CenterPoint = new PointData
            {
                X = Math.Round(pt.X, 4),
                Y = Math.Round(pt.Y, 4),
                Z = Math.Round(pt.Z, 4)
            };
        }

        // Get boundary segment count (for enclosed rooms)
        if (data.IsEnclosed)
        {
            try
            {
                var options = new SpatialElementBoundaryOptions();
                var boundaries = room.GetBoundarySegments(options);
                data.BoundaryLoopCount = boundaries?.Count ?? 0;
            }
            catch
            {
                // Boundary extraction can fail for some rooms
            }
        }

        // Get unbounded height
        var unboundedHeightParam = room.get_Parameter(BuiltInParameter.ROOM_UPPER_OFFSET);
        if (unboundedHeightParam != null && unboundedHeightParam.HasValue)
            data.UnboundedHeight = unboundedHeightParam.AsValueString();

        // Get department
        var deptParam = room.get_Parameter(BuiltInParameter.ROOM_DEPARTMENT);
        if (deptParam != null && deptParam.HasValue)
        {
            var dept = deptParam.AsString();
            if (!string.IsNullOrEmpty(dept))
                data.Department = dept;
        }

        // Get occupancy
        var occupancyParam = room.get_Parameter(BuiltInParameter.ROOM_OCCUPANCY);
        if (occupancyParam != null && occupancyParam.HasValue)
        {
            var occupancy = occupancyParam.AsString();
            if (!string.IsNullOrEmpty(occupancy))
                data.Occupancy = occupancy;
        }

        return data;
    }

    private sealed class PointData
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
    }

    private sealed class RoomData
    {
        public long Id { get; set; }
        public string? Name { get; set; }
        public string? Number { get; set; }
        public string? Level { get; set; }
        public long? LevelId { get; set; }
        public double? Area { get; set; }
        public string? AreaFormatted { get; set; }
        public double? Volume { get; set; }
        public string? VolumeFormatted { get; set; }
        public double? Perimeter { get; set; }
        public string? PerimeterFormatted { get; set; }
        public bool IsEnclosed { get; set; }
        public int? BoundaryLoopCount { get; set; }
        public PointData? CenterPoint { get; set; }
        public string? UnboundedHeight { get; set; }
        public string? Department { get; set; }
        public string? Occupancy { get; set; }
    }

    private sealed class RoomSummary
    {
        public double TotalArea { get; set; }
        public int EnclosedRooms { get; set; }
        public int NotEnclosedRooms { get; set; }
    }

    private sealed class GetRoomInfoResult
    {
        public List<RoomData> Rooms { get; set; } = new();
        public int Count { get; set; }
        public string? LevelFilter { get; set; }
        public bool Truncated { get; set; }
        public string? TruncatedMessage { get; set; }
        public RoomSummary? Summary { get; set; }
    }
}
