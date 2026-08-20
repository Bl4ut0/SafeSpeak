using System.Speech.Synthesis;
using Microsoft.Win32;

namespace SafeSpeak.Core.Audio;

/// <summary>
/// Unlocks and bridges modern Windows 10/11 OneCore Natural Voices into the desktop speech synthesizer.
/// </summary>
public static class OneCoreVoiceBridge
{
    private const string OneCoreRegistryPath = @"SOFTWARE\Microsoft\Speech_OneCore\Voices\Tokens";
    private const string SapiRegistryPath = @"SOFTWARE\Microsoft\Speech\Voices\Tokens";

    public static void UnlockOneCoreVoices()
    {
        try
        {
            using var oneCoreKey = Registry.LocalMachine.OpenSubKey(OneCoreRegistryPath);
            if (oneCoreKey == null) return;

            using var sapiKey = Registry.LocalMachine.OpenSubKey(SapiRegistryPath, writable: true);
            if (sapiKey == null) return;

            foreach (var voiceTokenName in oneCoreKey.GetSubKeyNames())
            {
                // Check if voice is already linked
                if (sapiKey.OpenSubKey(voiceTokenName) != null) continue;

                try
                {
                    using var srcVoice = oneCoreKey.OpenSubKey(voiceTokenName);
                    if (srcVoice == null) continue;

                    using var destVoice = sapiKey.CreateSubKey(voiceTokenName);
                    CopyRegistryKey(srcVoice, destVoice);
                }
                catch { }
            }
        }
        catch { }
    }

    private static void CopyRegistryKey(RegistryKey src, RegistryKey dest)
    {
        foreach (var valName in src.GetValueNames())
        {
            var val = src.GetValue(valName);
            var kind = src.GetValueKind(valName);
            if (val != null) dest.SetValue(valName, val, kind);
        }

        foreach (var subName in src.GetSubKeyNames())
        {
            using var srcSub = src.OpenSubKey(subName);
            if (srcSub == null) continue;
            using var destSub = dest.CreateSubKey(subName);
            CopyRegistryKey(srcSub, destSub);
        }
    }
}
