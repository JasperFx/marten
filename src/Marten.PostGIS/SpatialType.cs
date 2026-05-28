namespace Marten.PostGIS;

/// <summary>
/// PostGIS spatial column type.
/// </summary>
public enum SpatialType
{
    /// <summary>
    /// Geodetic coordinate system (lat/lon on Earth's surface).
    /// Distances in meters. Accurate for global data.
    /// This is the default.
    /// </summary>
    Geography,

    /// <summary>
    /// Cartesian coordinate system (projected plane).
    /// Faster operations but distances are in the coordinate system's units.
    /// </summary>
    Geometry
}
