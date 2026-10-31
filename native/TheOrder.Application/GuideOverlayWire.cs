using System.Text;

namespace TheOrder.Application;

// Little-endian bounded wire format shared with the native renderer. No object deserialization in a game.
public static class GuideOverlayWire
{
    public const int Version = 4, Magic = 0x31474f54, HeaderBytes = 256, MaximumBytes = 16 * 1024 * 1024;
    public static byte[] Encode(GuideOverlayDocument document, IReadOnlyList<string>? guides = null)
    {
        using var output = new MemoryStream(); using var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);
        void Text(string value)
        {
            if (value.Length > 32768 || value.Contains('\0')) throw new InvalidDataException("Overlay text exceeds limits");
            var bytes = Encoding.UTF8.GetBytes(value); writer.Write(bytes.Length); writer.Write(bytes);
        }
        Text(document.Title); Text(document.Attribution); Text(document.Status);
        if (guides?.Count > 128 || document.Routes.Length > 2048) throw new InvalidDataException("Overlay collection exceeds limits");
        writer.Write(guides?.Count ?? 0); foreach (var guide in guides ?? []) Text(guide);
        writer.Write(document.Routes.Length); int steps = 0;
        foreach (var route in document.Routes)
        {
            Text(route.Key); Text(route.Title); Text(route.From); Text(route.To); writer.Write(route.Steps.Length);
            foreach (var step in route.Steps)
            {
                if (++steps > 16384) throw new InvalidDataException("Too many overlay steps");
                Text(step.Kind); Text(step.Original); Text(step.Display); writer.Write(step.Machine ? 1 : 0);
                Text(step.SaveKey); writer.Write(step.Achievement ? 1 : 0); writer.Write(step.Heading ? 1 : 0);
                var alternatives = step.Alternatives.IsDefault ? [] : step.Alternatives;
                if (alternatives.Length > 128) throw new InvalidDataException("Too many alternative choices");
                writer.Write(alternatives.Length);
                foreach (var alternative in alternatives) { Text(alternative.Original); Text(alternative.Display); writer.Write(alternative.Machine ? 1 : 0); }
            }
        }
        writer.Write(document.Flow is null?0:1);
        if(document.Flow is { } flow)
        {
            void Number(float value) { if(!float.IsFinite(value)||value<0||value>32768)throw new InvalidDataException("Invalid flow geometry");writer.Write(value); }
            if(flow.Nodes.Length>512||flow.Edges.Length>768)throw new InvalidDataException("Flow exceeds limits");
            Number(flow.Width);Number(flow.Height);writer.Write(flow.Truncated?1:0);writer.Write(flow.Nodes.Length);
            foreach(var node in flow.Nodes) {Text(node.Kind);Text(node.Label);Text(node.EndingType);Number(node.X);Number(node.Y);Number(node.Width);Number(node.Height);}
            writer.Write(flow.Edges.Length);int points=0;
            foreach(var edge in flow.Edges)
            {
                if(edge.From<0||edge.From>=flow.Nodes.Length||edge.To<0||edge.To>=flow.Nodes.Length||edge.Route<0||edge.Route>=document.Routes.Length
                    ||edge.Start<0||edge.Count<0||edge.Start>document.Routes[edge.Route].Steps.Length-edge.Count||edge.Choices<0||edge.Choices>edge.Count
                    ||edge.Section<0||edge.Section>511||edge.Points.Length is <4 or >1024||(edge.Points.Length-1)%3!=0||(points+=edge.Points.Length)>32768)throw new InvalidDataException("Invalid flow reference");
                writer.Write(edge.From);writer.Write(edge.To);writer.Write(edge.Route);writer.Write(edge.Start);writer.Write(edge.Count);writer.Write(edge.Choices);writer.Write(edge.Section);writer.Write(edge.Unlock?1:0);
                writer.Write(edge.Points.Length);foreach(var point in edge.Points){Number(point.X);Number(point.Y);}
                var label=edge.Label??edge.Points[0];Number(label.X);Number(label.Y);
            }
        }
        writer.Write(document.Reading is null ? 0 : 1);
        if (document.Reading is { } reading)
        {
            if (reading.Channel is < 0 or > 2 || reading.Files.Length > 128 || reading.File < 0 || reading.File > 127 || reading.Page < 0 || reading.Page > 2000 || reading.Pages is < 0 or > 2000) throw new InvalidDataException("Invalid reader position");
            writer.Write(reading.Channel); writer.Write(reading.HasNetwork ? 1 : 0); writer.Write(reading.HasGuides ? 1 : 0); writer.Write(reading.HasNotes ? 1 : 0);
            writer.Write(reading.Files.Length); foreach (var file in reading.Files) Text(file);
            writer.Write(reading.File); writer.Write(reading.Page); writer.Write(reading.Pages); Text(reading.Notice);
            var page = reading.Content; Text(page?.Label ?? ""); var blocks = page?.Blocks ?? [];
            if (blocks.Length > 2048) throw new InvalidDataException("Too many reading blocks"); writer.Write(blocks.Length);
            foreach (var block in blocks)
            {
                Text(block.Kind); Text(block.Text); var rows = block.Rows.IsDefault ? [] : block.Rows;
                if (rows.Length > 128 || rows.Any(r => r.Length > 128)) throw new InvalidDataException("Table exceeds limits");
                writer.Write(rows.Length); foreach (var row in rows) { writer.Write(row.Length); foreach (var cell in row) Text(cell); }
            }
            int width = page?.ImageWidth ?? 0, height = page?.ImageHeight ?? 0; var rgba = page?.Rgba ?? [];
            if (width < 0 || height < 0 || width > 4096 || height > 4096 || (long)width * height > 3_000_000 || rgba.Length != (long)width * height * 4) throw new InvalidDataException("Invalid page pixels");
            writer.Write(width); writer.Write(height); writer.Write(rgba.Length); writer.Write(rgba);
            if (!float.IsFinite(reading.Progress) || reading.Progress is < 0 or > 1 || !float.IsFinite(reading.Zoom) || reading.Zoom is < .6f or > 4) throw new InvalidDataException("Invalid reading position");
            writer.Write(reading.Progress); writer.Write(reading.Zoom);
        }
        writer.Flush(); if (output.Length > MaximumBytes - HeaderBytes) throw new InvalidDataException("Overlay document exceeds limits");
        return output.ToArray();
    }
}

public interface IGameGuideLaunch
{
    Task PrepareForGameAsync(string gameId, CancellationToken cancellation) => Task.CompletedTask;
    IGameGuideLaunchPlan? Prepare(LaunchSnapshot snapshot);
}
public interface IGameGuideLaunchPlan
{
    IReadOnlyDictionary<string, string> Environment { get; }
    // Called only for a newly owned, suspended process. Failure must never prevent its resume.
    void Attach(int pid, long creationFileTime);
    void AttachObserved(ProcessIdentity identity) { }
    void FailedBeforeCreation();
}
