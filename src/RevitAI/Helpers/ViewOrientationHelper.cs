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

using Autodesk.Revit.DB;

namespace RevitAI.Helpers;

/// <summary>
/// Helper class for calculating 3D view orientation presets.
/// </summary>
public static class ViewOrientationHelper
{
    /// <summary>
    /// Standard distance from target for eye position calculations.
    /// </summary>
    private const double ViewDistance = 100.0;

    /// <summary>
    /// Angle for isometric views (in radians) - approximately 35.264 degrees (arctan(1/sqrt(2))).
    /// </summary>
    private static readonly double IsometricAngle = Math.Atan(1.0 / Math.Sqrt(2.0));

    /// <summary>
    /// Valid orientation preset names.
    /// </summary>
    public static readonly IReadOnlyList<string> ValidPresets = new[]
    {
        "isometric",
        "isometric_ne",
        "isometric_nw",
        "isometric_se",
        "isometric_sw",
        "front",
        "back",
        "left",
        "right",
        "top",
        "bottom"
    };

    /// <summary>
    /// Gets a ViewOrientation3D for the specified preset.
    /// </summary>
    /// <param name="preset">The preset name (case-insensitive).</param>
    /// <param name="target">The target point to look at (typically model center).</param>
    /// <returns>The ViewOrientation3D, or null if preset is invalid.</returns>
    public static ViewOrientation3D? GetPresetOrientation(string preset, XYZ target)
    {
        var normalizedPreset = preset.ToLowerInvariant().Replace("-", "_");

        return normalizedPreset switch
        {
            "isometric" or "isometric_se" => CreateIsometricOrientation(target, 1, -1),  // SE corner (default)
            "isometric_ne" => CreateIsometricOrientation(target, 1, 1),                   // NE corner
            "isometric_nw" => CreateIsometricOrientation(target, -1, 1),                  // NW corner
            "isometric_sw" => CreateIsometricOrientation(target, -1, -1),                 // SW corner
            "front" => CreateOrthographicOrientation(target, new XYZ(0, 1, 0)),           // Looking north (from south)
            "back" => CreateOrthographicOrientation(target, new XYZ(0, -1, 0)),          // Looking south (from north)
            "left" => CreateOrthographicOrientation(target, new XYZ(1, 0, 0)),           // Looking east (from west)
            "right" => CreateOrthographicOrientation(target, new XYZ(-1, 0, 0)),         // Looking west (from east)
            "top" => CreateTopOrientation(target),                                         // Looking down
            "bottom" => CreateBottomOrientation(target),                                   // Looking up
            _ => null
        };
    }

    /// <summary>
    /// Validates whether a preset name is valid.
    /// </summary>
    /// <param name="preset">The preset name to validate.</param>
    /// <returns>True if the preset is valid.</returns>
    public static bool IsValidPreset(string preset)
    {
        var normalizedPreset = preset.ToLowerInvariant().Replace("-", "_");
        return ValidPresets.Contains(normalizedPreset);
    }

    /// <summary>
    /// Creates an isometric view orientation from a corner.
    /// </summary>
    /// <param name="target">The target point to look at.</param>
    /// <param name="xSign">Sign for X direction (-1 or 1).</param>
    /// <param name="ySign">Sign for Y direction (-1 or 1).</param>
    private static ViewOrientation3D CreateIsometricOrientation(XYZ target, int xSign, int ySign)
    {
        // Calculate the horizontal angle (45 degrees between axes)
        var horizontalAngle = Math.Atan2(ySign, xSign);

        // Calculate eye position
        // For isometric: eye is at 45 degrees around Z-axis from target, elevated at IsometricAngle
        var cosH = Math.Cos(horizontalAngle);
        var sinH = Math.Sin(horizontalAngle);
        var cosV = Math.Cos(IsometricAngle);
        var sinV = Math.Sin(IsometricAngle);

        var eyeOffset = new XYZ(
            ViewDistance * cosV * cosH,
            ViewDistance * cosV * sinH,
            ViewDistance * sinV
        );

        var eyePosition = target + eyeOffset;

        // Forward direction (from eye to target, normalized)
        var forwardDirection = (target - eyePosition).Normalize();

        // Calculate up direction orthogonal to forward, staying close to world Z
        // right = forward × world_up, then up = right × forward
        var worldUp = XYZ.BasisZ;
        var rightDirection = forwardDirection.CrossProduct(worldUp).Normalize();
        var upDirection = rightDirection.CrossProduct(forwardDirection).Normalize();

        return new ViewOrientation3D(eyePosition, upDirection, forwardDirection);
    }

    /// <summary>
    /// Creates an orthographic (side) view orientation.
    /// </summary>
    /// <param name="target">The target point to look at.</param>
    /// <param name="viewDirection">The direction the view looks (from eye to target).</param>
    private static ViewOrientation3D CreateOrthographicOrientation(XYZ target, XYZ viewDirection)
    {
        // Eye is behind the target along the negative view direction
        var eyePosition = target - viewDirection * ViewDistance;

        // Forward direction is the view direction
        var forwardDirection = viewDirection;

        // Up direction is world Z
        var upDirection = XYZ.BasisZ;

        return new ViewOrientation3D(eyePosition, upDirection, forwardDirection);
    }

    /// <summary>
    /// Creates a top-down view orientation.
    /// </summary>
    /// <param name="target">The target point to look at.</param>
    private static ViewOrientation3D CreateTopOrientation(XYZ target)
    {
        // Eye is above target
        var eyePosition = target + new XYZ(0, 0, ViewDistance);

        // Looking down (negative Z)
        var forwardDirection = -XYZ.BasisZ;

        // Up direction is positive Y (north)
        var upDirection = XYZ.BasisY;

        return new ViewOrientation3D(eyePosition, upDirection, forwardDirection);
    }

    /// <summary>
    /// Creates a bottom-up view orientation.
    /// </summary>
    /// <param name="target">The target point to look at.</param>
    private static ViewOrientation3D CreateBottomOrientation(XYZ target)
    {
        // Eye is below target
        var eyePosition = target - new XYZ(0, 0, ViewDistance);

        // Looking up (positive Z)
        var forwardDirection = XYZ.BasisZ;

        // Up direction is positive Y (north)
        var upDirection = XYZ.BasisY;

        return new ViewOrientation3D(eyePosition, upDirection, forwardDirection);
    }

    /// <summary>
    /// Rotates a ViewOrientation3D around the world Z-axis by the specified angle.
    /// </summary>
    /// <param name="orientation">The current orientation.</param>
    /// <param name="angleDegrees">The rotation angle in degrees (positive = counter-clockwise when viewed from above).</param>
    /// <returns>The rotated orientation.</returns>
    public static ViewOrientation3D RotateHorizontal(ViewOrientation3D orientation, double angleDegrees)
    {
        var angleRadians = angleDegrees * Math.PI / 180.0;
        var rotation = Transform.CreateRotation(XYZ.BasisZ, angleRadians);

        var newEyePosition = rotation.OfPoint(orientation.EyePosition);
        var newForward = rotation.OfVector(orientation.ForwardDirection);
        var newUp = rotation.OfVector(orientation.UpDirection);

        return new ViewOrientation3D(newEyePosition, newUp, newForward);
    }

    /// <summary>
    /// Rotates a ViewOrientation3D vertically (pitch) around the right axis.
    /// </summary>
    /// <param name="orientation">The current orientation.</param>
    /// <param name="target">The target point being viewed.</param>
    /// <param name="angleDegrees">The rotation angle in degrees (positive = up).</param>
    /// <returns>The rotated orientation.</returns>
    public static ViewOrientation3D RotateVertical(ViewOrientation3D orientation, XYZ target, double angleDegrees)
    {
        var angleRadians = angleDegrees * Math.PI / 180.0;

        // Calculate the right axis (perpendicular to forward and up)
        var rightAxis = orientation.ForwardDirection.CrossProduct(orientation.UpDirection).Normalize();

        // Create rotation around the right axis, centered on target
        var rotation = Transform.CreateRotationAtPoint(rightAxis, angleRadians, target);

        var newEyePosition = rotation.OfPoint(orientation.EyePosition);
        var newForward = rotation.OfVector(orientation.ForwardDirection);
        var newUp = rotation.OfVector(orientation.UpDirection);

        return new ViewOrientation3D(newEyePosition, newUp, newForward);
    }
}
