using System.Media;
namespace WpfCSharp.Services;
public class SoundService
{
    public void PlayAlert()
    {
        try { SystemSounds.Exclamation.Play(); } catch { }
    }
}
