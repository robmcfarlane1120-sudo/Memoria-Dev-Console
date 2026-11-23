using System;
using System.Reflection;
using Memoria.Prime;
using UnityEngine;

namespace Memoria.DevConsole
{
    internal static class SceneTransitionAudio
    {
        private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly FieldInfo Active = typeof(MusicPlayer).GetField("activeSoundProfile", Fields);
        private static readonly FieldInfo Upcoming = typeof(MusicPlayer).GetField("upcomingSoundProfile", Fields);
        private static readonly FieldInfo Pending = typeof(MusicPlayer).GetField("onTheFlyLoadedSoundProfile", Fields);
        private static Single _guardUntil;
        private static Int32 _titleIndex;
        private static Boolean _reachedDestination;

        internal static void Prepare()
        {
            // Run before parsing the destination state or changing its mode.
            Boolean leavingTitle = PersistenSingleton<UIManager>.Instance != null &&
                PersistenSingleton<UIManager>.Instance.UnityScene == UIManager.Scene.Title;
            _guardUntil = 0f;
            _reachedDestination = false;
            StopStandalone(SoundLib.MusicPlayer);
            StopStandalone(SoundLib.MovieAudioPlayer);
            if (leavingTitle)
            {
                _titleIndex = SoundLib.GetMovieSoundIndex("FMV000");
                _guardUntil = Time.realtimeSinceStartup + 15f;
                Log.Message("[Dev Console][Audio] Cleared title music for scene transition.");
            }
        }

        private static void StopStandalone(MusicPlayer player)
        {
            if (player == null) return;
            // StopMusic does not clear this callback's pending profile in Memoria.
            if (Pending != null) Pending.SetValue(player, null);
            player.StopMusic(0);
        }

        internal static void Update()
        {
            if (_guardUntil == 0f) return;
            if (Time.realtimeSinceStartup >= _guardUntil) { _guardUntil = 0f; return; }
            UIManager ui = PersistenSingleton<UIManager>.Instance;
            if (ui != null)
            {
                if (ui.UnityScene == UIManager.Scene.Title && _reachedDestination)
                {
                    _guardUntil = 0f; // A deliberate return to the title screen.
                    return;
                }
                if (!_reachedDestination && (ui.UnityScene == UIManager.Scene.Field || ui.UnityScene == UIManager.Scene.World))
                {
                    _reachedDestination = true;
                    _guardUntil = Math.Min(_guardUntil, Time.realtimeSinceStartup + 2f);
                }
            }
            // TitleUI can request FMV000 again while its old scene is fading out.
            // Only remove that standalone MovieAudio profile, never field/world BGM.
            MusicPlayer player = SoundLib.MusicPlayer;
            if (player == null) return;
            ClearTitleProfile(player, Pending, false);
            ClearTitleProfile(player, Active, true);
            ClearTitleProfile(player, Upcoming, true);
        }

        private static void ClearTitleProfile(MusicPlayer player, FieldInfo field, Boolean stop)
        {
            if (field == null) return;
            SoundProfile profile = field.GetValue(player) as SoundProfile;
            if (profile == null || profile.SoundProfileType != SoundProfileType.MovieAudio ||
                _titleIndex < 0 || profile.SoundIndex != _titleIndex) return;
            if (stop)
            {
                ISdLibAPIProxy.Instance.SdSoundSystem_SoundCtrl_Stop(profile.SoundID, 0);
                profile.SoundProfileState = SoundProfileState.Stopped;
            }
            field.SetValue(player, null);
        }
    }
}
