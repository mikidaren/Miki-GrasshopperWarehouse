using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Rhino.Geometry;

internal static class PreviewRenderer
{
    // Orthographic software preview of the actual validated native geometry.
    public static void Render(string path, string title, Miki.IceWorld.Grasshopper.NativeExample model)
    {
        var triangles = new List<(Point3d[] Points, string Role)>();
        for (int i = 0; i < model.Geometry.Length; i++)
        foreach (var mesh in Mesh.CreateFromBrep(model.Geometry[i], MeshingParameters.FastRenderMesh))
        foreach (var face in mesh.Faces)
        {
            Point3d[] points = face.IsQuad
                ? [mesh.Vertices[face.A], mesh.Vertices[face.B], mesh.Vertices[face.C], mesh.Vertices[face.D]]
                : [mesh.Vertices[face.A], mesh.Vertices[face.B], mesh.Vertices[face.C]];
            triangles.Add((points, model.Roles[i]));
        }
        (double X, double Y) Project(Point3d p) => (.70710678 * (p.X + p.Y), .40824829 * (p.X - p.Y) - .81649658 * p.Z);
        var projected = triangles.SelectMany(t => t.Points).Select(Project).ToArray();
        double minX = projected.Min(p => p.X), maxX = projected.Max(p => p.X), minY = projected.Min(p => p.Y), maxY = projected.Max(p => p.Y);
        double scale = Math.Min(1040 / (maxX - minX), 620 / (maxY - minY));
        PointF Screen(Point3d p)
        {
            var q = Project(p);
            return new((float)(600 + (q.X - (minX + maxX) / 2) * scale), (float)(460 + (q.Y - (minY + maxY) / 2) * scale));
        }
        using var image = new Bitmap(1200, 900); using var graphics = Graphics.FromImage(image);
        graphics.Clear(Color.FromArgb(244, 248, 250)); graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var titleFont = new Font("Segoe UI", 25, FontStyle.Bold); using var noteFont = new Font("Segoe UI", 13);
        graphics.DrawString(title, titleFont, Brushes.DarkSlateGray, 48, 38);
        graphics.DrawString("MIKI / ICEWORLD     Parametric concept study", noteFont, Brushes.SlateGray, 50, 85);
        foreach (var triangle in triangles.OrderBy(t => t.Points.Average(p => p.X - p.Y + p.Z)))
        {
            var normal = Vector3d.CrossProduct(triangle.Points[1] - triangle.Points[0], triangle.Points[2] - triangle.Points[0]); normal.Unitize();
            double shade = .7 + .3 * Math.Abs(normal.Z);
            var color = triangle.Role is "handrail" or "post" or "ornament" ? Color.FromArgb(31, 102, 121) : Color.FromArgb(151, 207, 226);
            using var brush = new SolidBrush(Color.FromArgb((int)(color.R * shade), (int)(color.G * shade), (int)(color.B * shade)));
            graphics.FillPolygon(brush, triangle.Points.Select(Screen).ToArray());
        }
        graphics.DrawString($"{model.Geometry.Length} closed Breps  /  orthographic projection of native geometry", noteFont, Brushes.SlateGray, 50, 805);
        graphics.DrawString("Test dimensions. Project dimensions and visitor capacity remain undecided.", noteFont, Brushes.SlateGray, 50, 836);
        image.Save(path, ImageFormat.Png);
    }
}
