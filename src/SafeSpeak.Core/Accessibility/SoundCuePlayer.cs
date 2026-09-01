using System.Media;

namespace SafeSpeak.Core.Accessibility;

/// <summary>
/// Plays discrete system earcons and audio feedback for blind and low-vision streamers.
/// </summary>
public static class SoundCuePlayer
{
    public static void PlayCue(SoundCueType cueType)
    {
        Task.Run(() =>
        {
            try
            {
                switch (cueType)
                {
                    case SoundCueType.Armed:
                        Console.Beep(880, 80);
                        Console.Beep(1320, 100);
                        break;
                    case SoundCueType.Disarmed:
                        Console.Beep(1320, 80);
                        Console.Beep(880, 100);
                        break;
                    case SoundCueType.MessageApproved:
                        Console.Beep(1046, 50);
                        break;
                    case SoundCueType.MessageBlocked:
                        Console.Beep(440, 70);
                        break;
                    case SoundCueType.TikFinityConnected:
                        Console.Beep(523, 70);
                        Console.Beep(659, 70);
                        Console.Beep(784, 90);
                        break;
                    case SoundCueType.TikFinityDisconnected:
                        Console.Beep(784, 70);
                        Console.Beep(659, 70);
                        Console.Beep(523, 90);
                        break;
                    case SoundCueType.EmergencyStop:
                        Console.Beep(1200, 120);
                        Console.Beep(600, 150);
                        break;
                    case SoundCueType.QueueEmpty:
                        SystemSounds.Asterisk.Play();
                        break;
                }
            }
            catch
            {
                // Fallback to system sounds if beep fails
                try { SystemSounds.Beep.Play(); } catch { }
            }
        });
    }
}
