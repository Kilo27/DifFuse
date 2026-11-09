using System.Text;
using DifFuse.Core.Library;
using DifFuse.Core.Models;

namespace DifFuse.Core.Tests;

public class FuseFileTests
{
    private static FuseDocument ParseText(string xml) => FuseFile.Parse(new MemoryStream(Encoding.UTF8.GetBytes(xml)));

    private static readonly Preset Sample = new()
    {
        Name = "Test Lead",
        Amp = new AmpSettings
        {
            ModelId = 0x5d, Volume = 200, Gain = 180, Gain2 = 90, MasterVolume = 150, Treble = 140,
            Middle = 100, Bass = 120, Presence = 160, Depth = 70, Bias = 130, NoiseGate = 5, Threshold = 6,
            Cabinet = Cabinets.V4x12, Sag = 2, Brightness = true, UsbGain = 77,
        },
        Stomp = new EffectSettings { Family = EffectFamily.Stomp, ModelId = 0x88, Knobs = new KnobValues(2, 0, 0, 0, 0, 0), Slot = 0 },
        Modulation = new EffectSettings { Family = EffectFamily.Modulation, ModelId = 0x101f, Knobs = new KnobValues(100, 21, 11, 8, 50, 0), Slot = 1, Enabled = false },
        Delay = new EffectSettings { Family = EffectFamily.Delay, ModelId = 0x2a, Knobs = new KnobValues(1, 2, 3, 4, 5, 6), Slot = 6 },
        Reverb = new EffectSettings { Family = EffectFamily.Reverb, ModelId = 0x0b, Knobs = new KnobValues(90, 80, 70, 60, 50, 0), Slot = 7 },
    };

    [Fact]
    public void Written_file_reads_back_to_the_same_preset()
    {
        var xml = FuseFile.ToXml(Sample, "me").ToString();
        var doc = ParseText(xml);

        Assert.True(doc.HasSound);
        Assert.Equal("Test Lead", doc.Name);
        Assert.Equal("me", doc.Author);
        Assert.Equal(Sample, doc.Preset);
    }

    [Fact]
    public void Writer_uses_the_fuse_layout()
    {
        var xml = FuseFile.ToXml(Sample).Root!;
        Assert.Equal("Preset", xml.Name.LocalName);
        var amp = xml.Element("Amplifier")!.Element("Module")!;
        Assert.Equal("93", (string?)amp.Attribute("ID"));
        Assert.Equal(23, amp.Elements("Param").Count());
        Assert.Equal(((200 << 8) | 200).ToString(), amp.Elements("Param").First().Value);

        var fx = xml.Element("FX")!;
        Assert.Equal(["Stompbox", "Modulation", "Delay", "Reverb"], fx.Elements().Select(e => e.Name.LocalName));
        var simpleComp = fx.Element("Stompbox")!.Element("Module")!;
        Assert.Single(simpleComp.Elements("Param"));
        var tape = fx.Element("Delay")!.Element("Module")!;
        Assert.Equal(6, tape.Elements("Param").Count());
        Assert.Equal("6", (string?)tape.Attribute("POS"));

        var diatonic = fx.Element("Modulation")!.Element("Module")!;
        Assert.Equal("287", (string?)diatonic.Attribute("ID")); // 0x11f, as PLUG writes it
        Assert.Equal("0", (string?)diatonic.Attribute("BypassState"));

        Assert.Equal("77", xml.Element("UsbGain")!.Value);
    }

    [Fact]
    public void Reads_fuse_style_file_with_plain_selector_values()
    {
        const string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <Preset amplifier="Mustang I/II" ProductId="1">
              <Amplifier>
                <Module ID="83" POS="0" BypassState="1">
                  <Param ControlIndex="0">51400</Param>
                  <Param ControlIndex="1">32896</Param>
                  <Param ControlIndex="4">65535</Param>
                  <Param ControlIndex="15">3</Param>
                  <Param ControlIndex="17">3</Param>
                  <Param ControlIndex="19">1</Param>
                  <Param ControlIndex="20">0</Param>
                </Module>
              </Amplifier>
              <FX>
                <Stompbox ID="1"><Module ID="0" POS="0" BypassState="1" /></Stompbox>
                <Modulation ID="2">
                  <Module ID="79" POS="5" BypassState="1">
                    <Param ControlIndex="0">25700</Param>
                    <Param ControlIndex="4">1</Param>
                  </Module>
                </Modulation>
                <Delay ID="3">
                  <Module ID="68" POS="6" BypassState="1">
                    <Param ControlIndex="4">2</Param>
                  </Module>
                </Delay>
                <Reverb ID="4"><Module ID="11" POS="7" BypassState="1"><Param ControlIndex="1">12850</Param></Module></Reverb>
              </FX>
              <FUSE><Info name="Spring Clean" author="Someone" rating="0" /></FUSE>
              <UsbGain>128</UsbGain>
            </Preset>
            """;
        var doc = ParseText(xml);
        var p = doc.Preset!;

        Assert.Equal("Spring Clean", doc.Name);
        Assert.Equal(0x53, p.Amp.ModelId);
        Assert.Equal(200, p.Amp.Volume);
        Assert.Equal(128, p.Amp.Gain);
        Assert.Equal(255, p.Amp.Treble);
        Assert.Equal(3, p.Amp.NoiseGate);
        Assert.Equal(1, p.Amp.Sag);
        Assert.False(p.Amp.Brightness);
        Assert.True(p.Stomp.IsEmpty);
        Assert.Equal(0x4f, p.Modulation.ModelId);
        Assert.Equal(100, p.Modulation.Knobs[0]);
        Assert.Equal(1, p.Modulation.Knobs[4]); // phaser shape, plain value
        Assert.Equal(2, p.Delay.Knobs[4]); // multitap mode, plain value
        Assert.Equal(50, p.Reverb.Knobs[1]);
        Assert.True(p.Modulation.IsPostAmp);
    }

    [Fact]
    public void Reads_plug_style_selector_values_stored_as_16_bit()
    {
        const string xml = """
            <Preset><Amplifier><Module ID="109" POS="0" BypassState="1" /></Amplifier>
            <FX><Delay ID="3"><Module ID="68" POS="2" BypassState="1"><Param ControlIndex="4">771</Param></Module></Delay></FX>
            <FUSE><Info name="x" /></FUSE></Preset>
            """;
        var p = ParseText(xml).Preset!;
        Assert.Equal(3, p.Delay.Knobs[4]);
        Assert.Equal(0x6d, p.Amp.ModelId);
        Assert.Equal(Cabinets.G4x12, p.Amp.Cabinet); // model default when the file has no cabinet
    }

    [Fact]
    public void Fuse_amp_data_file_has_a_name_but_no_sound()
    {
        const string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <FUSE>
              <Info name="Night Drive" author="Fender FUSE" rating="0" genre1="-1" genre2="-1" genre3="-1" tags="" fenderid="0">
              </Info>
              <PedalColors><Color ID="1">14</Color></PedalColors>
              <Band Type="0" Repeat="0"><SongFile Location="6">No Band</SongFile></Band>
            </FUSE>
            """;
        var doc = ParseText(xml);
        Assert.False(doc.HasSound);
        Assert.Equal("Night Drive", doc.Name);
        Assert.Equal("Fender FUSE", doc.Author);
        Assert.NotEmpty(doc.Warnings);
    }

    [Fact]
    public void Accepts_both_ids_of_the_diatonic_pitch_shifter()
    {
        foreach (var id in new[] { 0x11f, 0x101f })
        {
            var xml = $"""<Preset><Amplifier><Module ID="83" /></Amplifier><FX><Modulation><Module ID="{id}" POS="1" /></Modulation></FX></Preset>""";
            Assert.Equal(0x101f, ParseText(xml).Preset!.Modulation.ModelId);
        }
    }

    [Fact]
    public void Unknown_models_become_warnings_not_errors()
    {
        const string xml = """<Preset><Amplifier><Module ID="3" /></Amplifier><FX><Delay><Module ID="999" POS="6" /></Delay></FX></Preset>""";
        var doc = ParseText(xml);
        Assert.Equal(0x53, doc.Preset!.Amp.ModelId);
        Assert.True(doc.Preset.Delay.IsEmpty);
        Assert.Equal(2, doc.Warnings.Count);
    }

    [Fact]
    public void Colliding_positions_are_spread_out()
    {
        const string xml = """
            <Preset><Amplifier><Module ID="83" /></Amplifier><FX>
              <Stompbox><Module ID="60" POS="0" /></Stompbox>
              <Modulation><Module ID="18" POS="0" /></Modulation>
              <Delay><Module ID="22" POS="5" /></Delay>
              <Reverb><Module ID="36" POS="5" /></Reverb>
            </FX></Preset>
            """;
        var p = ParseText(xml).Preset!;
        Assert.Equal(4, p.Effects.Select(e => e.Slot).Distinct().Count());
        Assert.False(p.Modulation.IsPostAmp);
        Assert.True(p.Reverb.IsPostAmp);
    }

    [Fact]
    public void Invalid_xml_is_reported_as_invalid_data()
    {
        Assert.Throws<InvalidDataException>(() => ParseText("<Preset><Amplifier>"));
    }

    [Fact]
    public void Reads_the_users_old_fuse_amp_data_when_present()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Fender", "FUSE", "AmpData");
        if (!Directory.Exists(folder)) return;

        foreach (var file in Directory.GetFiles(folder, "*.fuse"))
        {
            var doc = FuseFile.Load(file);
            Assert.False(string.IsNullOrWhiteSpace(doc.Name));
        }
    }
}
