using DifFuse.Core.Device;
using DifFuse.Core.Models;
using DifFuse.Hid;

// DifFuse probe: checks the USB protocol against a real amp. It never writes to a preset slot.
//   list            show Mustang amps Windows can see
//   read            read-only: init handshake, preset names, current preset
//   listen SECONDS  print what the amp sends while you use its own controls
//   live            audible but temporary: loads a slot and back, nudges a few controls, restores them
var command = args.FirstOrDefault()?.ToLowerInvariant() ?? "read";
var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "DifFuse", "logs", $"probe-{command}-{DateTime.Now:yyyyMMdd-HHmmss}.log");

var locator = new WindowsHidLocator();
var devices = locator.FindMustangs();
Console.WriteLine($"Found {devices.Count} Mustang device(s).");
foreach (var d in devices)
{
    var info = DeviceInfo.FromProductId(d.ProductId);
    Console.WriteLine($"  PID 0x{d.ProductId:X4}  {info?.DisplayName ?? "?"}  product string \"{d.ProductName}\"");
    Console.WriteLine($"  {d.Path}");
}
if (command == "list") return 0;
if (devices.Count == 0)
{
    Console.WriteLine("No amp found. Is it plugged in and switched on?");
    return 1;
}

var descriptor = devices[0];
var device = DeviceInfo.FromProductId(descriptor.ProductId)!;
using var log = new PacketLog(filePath: logPath);
Console.WriteLine($"Logging packets to {logPath}");

using var transport = locator.Open(descriptor);
using var client = new MustangClient(transport, device, new ClientOptions(), log);
client.AmpPresetChanged += (_, e) =>
    Console.WriteLine($"  [amp] switched to slot {e.Slot:00} \"{e.Preset.Name}\" ({AmpCatalog.NameOf(e.Preset.Amp.ModelId)})");

var sw = System.Diagnostics.Stopwatch.StartNew();
var initial = await client.StartAsync();
Console.WriteLine($"Handshake and preset download took {sw.ElapsedMilliseconds} ms.");
Console.WriteLine($"{initial.PresetNames.Count} presets:");
for (int i = 0; i < initial.PresetNames.Count; i++)
{
    Console.WriteLine($"  {(i == initial.CurrentSlot ? '>' : ' ')} {i:00} {initial.PresetNames[i]}");
}
Console.WriteLine($"Current slot (from the name packet header): {initial.CurrentSlot:00}");
Print(initial.Current);

switch (command)
{
    case "read":
        break;

    case "listen":
        int seconds = args.Length > 1 && int.TryParse(args[1], out var s) ? s : 20;
        Console.WriteLine($"Listening for {seconds} s. Turn the amp's preset selector or knobs now.");
        int before = log.Snapshot().Count;
        await Task.Delay(TimeSpan.FromSeconds(seconds));
        var heard = log.Snapshot().Skip(before).Where(e => e.Direction == PacketDirection.FromAmp).ToList();
        Console.WriteLine($"The amp sent {heard.Count} packet(s) by itself:");
        foreach (var e in heard.Take(60)) Console.WriteLine("  " + PacketLog.FormatLine(e));
        break;

    case "live":
        await Live(client, initial);
        break;

    default:
        Console.WriteLine($"Unknown command '{command}'.");
        return 2;
}

Console.WriteLine("Done. Nothing was saved to the amp.");
return 0;

static async Task Live(MustangClient client, InitialState initial)
{
    var original = initial.Current;
    int otherSlot = (initial.CurrentSlot + 1) % initial.PresetNames.Count;

    Step($"Loading slot {otherSlot:00} \"{initial.PresetNames[otherSlot]}\" (you should hear that preset)");
    var other = await client.LoadSlotAsync(otherSlot);
    Console.WriteLine($"  got \"{other.Name}\" ({AmpCatalog.NameOf(other.Amp.ModelId)}), list says \"{initial.PresetNames[otherSlot]}\"");
    await Task.Delay(2500);

    Step($"Back to slot {initial.CurrentSlot:00} \"{initial.PresetNames[initial.CurrentSlot]}\"");
    var back = await client.LoadSlotAsync(initial.CurrentSlot);
    Console.WriteLine($"  matches the start-up read: amp {(back.Amp == original.Amp ? "yes" : "NO")}, " +
        $"effects {(EffectFamilies.All.All(f => back.Effect(f).ModelId == original.Effect(f).ModelId) ? "yes" : "NO")}");
    await Task.Delay(1500);

    var amp = back.Amp;
    byte darker = (byte)Math.Max(0, amp.Treble - 90);
    Step($"Treble {amp.Treble} -> {darker} (darker), sweeping like a knob");
    for (int t = amp.Treble; t >= darker; t -= 6)
    {
        client.QueueAmp(amp with { Treble = (byte)t });
        await Task.Delay(15);
    }
    client.QueueAmp(amp with { Treble = darker });
    await client.FlushAsync();
    await Task.Delay(2500);
    Step($"Treble back to {amp.Treble}");
    client.QueueAmp(amp);
    await client.FlushAsync();
    await Task.Delay(1500);

    var reverb = back.Reverb;
    if (reverb.IsActive)
    {
        byte lower = (byte)Math.Max(0, reverb.Knobs[0] - 80);
        Step($"Reverb ({EffectCatalog.NameOf(reverb.ModelId)}) level {reverb.Knobs[0]} -> {lower} (knob-only update, no clear)");
        client.QueueEffect(reverb with { Knobs = reverb.Knobs.With(0, lower) });
        await client.FlushAsync();
        await Task.Delay(2500);

        Step("Reverb bypassed");
        client.QueueEffect(reverb with { Enabled = false });
        await client.FlushAsync();
        await Task.Delay(2500);

        Step("Reverb on again with its original level");
        client.QueueEffect(reverb);
        await client.FlushAsync();
        await Task.Delay(1500);
    }
    else
    {
        Console.WriteLine("  (this preset has no reverb, skipping the effect steps)");
    }

    Step("Reloading the slot so the amp is exactly as it was");
    await client.LoadSlotAsync(initial.CurrentSlot);
}

static void Step(string text) => Console.WriteLine($"\n== {DateTime.Now:HH:mm:ss} {text}");

static void Print(Preset p)
{
    var a = p.Amp;
    Console.WriteLine($"Current preset \"{p.Name}\"");
    Console.WriteLine($"  Amp {AmpCatalog.NameOf(a.ModelId)} (0x{a.ModelId:X2}): gain {a.Gain} vol {a.Volume} treble {a.Treble} mid {a.Middle} bass {a.Bass}");
    Console.WriteLine($"      gain2 {a.Gain2} master {a.MasterVolume} presence {a.Presence} depth {a.Depth} bias {a.Bias} sag {a.Sag}");
    Console.WriteLine($"      gate {NoiseGates.Names[a.NoiseGate]} threshold {a.Threshold} cabinet {Cabinets.NameOf(a.Cabinet)} bright {a.Brightness} usb gain {a.UsbGain}");
    foreach (var e in p.Effects)
    {
        Console.WriteLine($"  {e.Family,-10} {EffectCatalog.NameOf(e.ModelId),-18} slot {e.Slot} {(e.IsPostAmp ? "post" : "pre ")} knobs {string.Join(' ', e.Knobs.ToArray())}");
    }
}
