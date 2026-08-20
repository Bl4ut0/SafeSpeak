using System.Text.Json.Serialization;

namespace SafeSpeak.Core.Audio.VoiceFramework;

public sealed class VoicePackageManifest
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = "";

    [JsonPropertyName("author")]
    public string Author { get; set; } = "Community / Creator";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0.0";

    [JsonPropertyName("culture")]
    public string Culture { get; set; } = "en-US";

    [JsonPropertyName("gender")]
    public string Gender { get; set; } = "Neutral";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "Custom User Cloned TTS Voice Pack for SafeSpeak";

    [JsonPropertyName("engineType")]
    public string EngineType { get; set; } = "PiperOnnx"; // PiperOnnx, Vits, XTTS, WebEndpoint

    [JsonPropertyName("modelFileName")]
    public string ModelFileName { get; set; } = "model.onnx";

    [JsonPropertyName("configFileName")]
    public string ConfigFileName { get; set; } = "model.onnx.json";

    [JsonPropertyName("sampleAudioFileName")]
    public string? SampleAudioFileName { get; set; } = "sample.wav";

    [JsonPropertyName("speakerId")]
    public int SpeakerId { get; set; } = 0;

    [JsonPropertyName("defaultSpeed")]
    public double DefaultSpeed { get; set; } = 1.0;

    [JsonPropertyName("defaultPitch")]
    public double DefaultPitch { get; set; } = 1.0;
}
