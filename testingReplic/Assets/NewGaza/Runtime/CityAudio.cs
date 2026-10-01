using System;
using NewGaza.Core;
using UnityEngine;

namespace NewGaza
{
    public enum CityAudioChannel
    {
        Master,
        Equipment,
        Ambience,
        Interface
    }

    /// <summary>
    /// Camera-aware native Unity audio. One spatial listener remains on the rendering
    /// camera; all source attenuation is authored from ground focus and orthographic zoom.
    /// </summary>
    public sealed class CityAudio : MonoBehaviour
    {
        private sealed class Voice
        {
            internal AudioSource Source;
            internal AudioLowPassFilter LowPass;
            internal float Gain;
            internal bool WasPlaying;
        }

        private struct MachineState
        {
            internal bool Active;
            internal Vector3 Position;
            internal float Load;
            internal float Movement;
            internal float Hydraulics;
            internal float Distance;
            internal float Attenuation;
        }

        private const float FadeSeconds = 0.24f;
        private const float ClickThrottleSeconds = 0.04f;
        private const float StopThreshold = 0.0015f;

        private readonly Voice[] engines = new Voice[3];
        private readonly Voice[] hydraulics = new Voice[3];
        private readonly Voice[] tracks = new Voice[3];
        private readonly Voice[] loops = new Voice[11];
        private readonly MachineState[] machines = new MachineState[3];
        private readonly float[] voiceTargets = new float[11];
        private readonly float[] channelVolumes = new float[4];
        private float[] coastX;
        private float[] coastZ;
        private Voice wind;
        private Voice surf;
        private Voice interfaceVoice;
        private AudioClip clickClip;
        private AudioClip confirmClip;
        private AudioClip windClip;
        private AudioClip surfClip;
        private AudioClip hydraulicsClip;
        private AudioClip tracksClip;
        private AudioClip[] engineClips;
        private GameSession session;
        private CityWorld world;
        private CityCamera cityCamera;
        private Camera renderingCamera;
        private int voiceCount;
        private bool initialized;
        private bool muted;
        private bool applicationPaused;
        private bool applicationUnfocused;
        private bool suspended;
        private float lastClickTime = -1f;

        public int AudioVoiceCount { get { return voiceCount; } }
        public int ActiveLoopCount
        {
            get
            {
                if (suspended) return 0;
                int count = 0;
                for (int i = 0; i < loops.Length; i++)
                    if (loops[i] != null && loops[i].Gain > StopThreshold) count++;
                return count;
            }
        }

        public bool IsMuted { get { return muted; } }

        public void Initialize(GameSession gameSession, CityWorld cityWorld,
            CityCamera cameraController, Camera camera)
        {
            if (gameSession == null) throw new ArgumentNullException(nameof(gameSession));
            if (cityWorld == null) throw new ArgumentNullException(nameof(cityWorld));
            if (cameraController == null) throw new ArgumentNullException(nameof(cameraController));
            if (camera == null) throw new ArgumentNullException(nameof(camera));
            if (initialized) throw new InvalidOperationException("CityAudio can only be initialized once.");

            session = gameSession;
            world = cityWorld;
            cityCamera = cameraController;
            renderingCamera = camera;
            for (int i = 0; i < channelVolumes.Length; i++)
                channelVolumes[i] = CityAudioPreferences.LoadVolume((CityAudioChannel)i);
            muted = CityAudioPreferences.LoadMuted();

            // Load every required sound exactly once. Missing authored assets are a
            // startup error, not a cue to substitute synthesized or silent audio.
            engineClips = new[]
            {
                LoadRequired("excavator_engine"),
                LoadRequired("truck_engine"),
                LoadRequired("dozer_engine")
            };
            hydraulicsClip = LoadRequired("hydraulics");
            tracksClip = LoadRequired("tracks");
            windClip = LoadRequired("wind_high");
            surfClip = LoadRequired("coastal_surf");
            clickClip = LoadRequired("ui_click");
            confirmClip = LoadRequired("ui_confirm");

            GeoPoint[] coastline = GameGeography.Coastline;
            coastX = new float[coastline.Length];
            coastZ = new float[coastline.Length];
            for (int i = 0; i < coastline.Length; i++)
            {
                coastX[i] = coastline[i].x;
                coastZ[i] = coastline[i].z;
            }

            AnimationCurve flatRolloff = new AnimationCurve(
                new Keyframe(0f, 1f), new Keyframe(1f, 1f));
            for (int i = 0; i < 3; i++)
            {
                engines[i] = CreateLoop("Engine " + i, engineClips[i], flatRolloff, true);
                hydraulics[i] = CreateLoop("Hydraulics " + i, hydraulicsClip, flatRolloff, true);
                tracks[i] = CreateLoop("Tracks " + i, tracksClip, flatRolloff, true);
                loops[i] = engines[i];
                loops[3 + i] = hydraulics[i];
                loops[6 + i] = tracks[i];
            }
            wind = CreateLoop("High desert air", windClip, flatRolloff);
            surf = CreateLoop("Mediterranean coast surf", surfClip, flatRolloff);
            loops[9] = wind;
            loops[10] = surf;
            interfaceVoice = CreateVoice("Interface sounds", false, flatRolloff);
            interfaceVoice.Source.spatialBlend = 0f;
            interfaceVoice.Source.priority = 32;
            interfaceVoice.Source.ignoreListenerPause = false;
            voiceCount = 12;
            initialized = true;
            ApplyMute();
        }

        private static AudioClip LoadRequired(string name)
        {
            AudioClip clip = Resources.Load<AudioClip>("Audio/" + name);
            if (clip == null)
                throw new InvalidOperationException(
                    "Missing required New Gaza audio clip Resources/Audio/" + name +
                    ".wav. Add the authored audio asset; procedural fallback is disabled.");
            return clip;
        }

        private Voice CreateLoop(string name, AudioClip clip, AnimationCurve flatRolloff,
            bool muffleAtDistance = false)
        {
            Voice voice = CreateVoice(name, true, flatRolloff);
            voice.Source.clip = clip;
            voice.Source.loop = true;
            if (muffleAtDistance)
            {
                voice.LowPass = voice.Source.gameObject.AddComponent<AudioLowPassFilter>();
                voice.LowPass.cutoffFrequency = 18000f;
                voice.LowPass.lowpassResonanceQ = 0.7f;
            }
            return voice;
        }

        private Voice CreateVoice(string name, bool spatial, AnimationCurve flatRolloff)
        {
            var sourceObject = new GameObject("Audio • " + name);
            sourceObject.transform.SetParent(transform, false);
            AudioSource source = sourceObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.dopplerLevel = 0f;
            source.spatialBlend = spatial ? 1f : 0f;
            source.rolloffMode = AudioRolloffMode.Custom;
            source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, flatRolloff);
            source.minDistance = 35f;
            source.maxDistance = CityAudioMix.MaximumSourceDistance;
            source.priority = spatial ? 128 : 32;
            source.mute = muted;
            return new Voice { Source = source };
        }

        private void Update()
        {
            if (!initialized || suspended || session == null || !session.Ready ||
                cityCamera == null || renderingCamera == null) return;

            Vector3 focus = cityCamera.AudioFocus;
            float zoom = CityAudioMix.SanitizeZoom(cityCamera.AudioZoom);
            CityFleet fleet = world.Fleet;
            float attenuationSum = 0f;
            float nearestDistance = CityAudioMix.MaximumSourceDistance;

            for (int i = 0; i < machines.Length; i++)
            {
                MachineState machine = machines[i];
                machine.Active = fleet != null && fleet.TryGetMachineAudioState(i,
                    out machine.Position, out machine.Load, out machine.Movement,
                    out machine.Hydraulics);
                if (machine.Active && IsFinite(machine.Position))
                {
                    machine.Distance = CityAudioMix.HorizontalDistance(focus.x, focus.z,
                        machine.Position.x, machine.Position.z);
                    machine.Attenuation = CityAudioMix.FocusAttenuation(machine.Distance, zoom);
                    machine.Load = CityAudioMix.Clamp01Finite(machine.Load);
                    machine.Movement = CityAudioMix.Clamp01Finite(machine.Movement);
                    machine.Hydraulics = CityAudioMix.Clamp01Finite(machine.Hydraulics);
                    attenuationSum += machine.Attenuation;
                    if (machine.Distance < nearestDistance) nearestDistance = machine.Distance;
                }
                else
                {
                    machine.Active = false;
                    machine.Position = focus;
                    machine.Load = machine.Movement = machine.Hydraulics = 0f;
                    machine.Distance = CityAudioMix.MaximumSourceDistance;
                    machine.Attenuation = 0f;
                }
                machines[i] = machine;
            }

            for (int i = 0; i < machines.Length; i++)
            {
                MachineState machine = machines[i];
                CityAudioMix.MachineMix mix = CityAudioMix.EvaluateMachine(machine.Active, i,
                    machine.Distance, zoom, machine.Load, machine.Movement,
                    machine.Hydraulics, attenuationSum - machine.Attenuation);
                voiceTargets[i] = mix.Engine;
                voiceTargets[3 + i] = mix.Hydraulics;
                voiceTargets[6 + i] = mix.Tracks;
            }

            CityAudioMix.NearestPolylinePoint(focus.x, focus.z, coastX, coastZ,
                coastX.Length, out float shoreX, out float shoreZ);
            float coastDistance = CityAudioMix.HorizontalDistance(focus.x, focus.z, shoreX, shoreZ);
            CityAudioMix.AmbienceMix ambience = CityAudioMix.EvaluateAmbience(zoom,
                nearestDistance, coastDistance);
            voiceTargets[9] = ambience.Wind;
            voiceTargets[10] = ambience.CoastalSurf;

            float totalEnergy = 0f;
            for (int i = 0; i < voiceTargets.Length; i++) totalEnergy += voiceTargets[i];
            float energyScale = CityAudioMix.TotalEnergyScale(totalEnergy);

            for (int i = 0; i < machines.Length; i++)
            {
                MachineState machine = machines[i];
                Vector3 sourcePosition = machine.Active ? machine.Position : focus;
                UpdateLoop(engines[i], voiceTargets[i] * energyScale,
                    CityAudioChannel.Equipment, sourcePosition, machine.Distance, zoom);
                UpdateLoop(hydraulics[i], voiceTargets[3 + i] * energyScale,
                    CityAudioChannel.Equipment, sourcePosition, machine.Distance, zoom);
                UpdateLoop(tracks[i], voiceTargets[6 + i] * energyScale,
                    CityAudioChannel.Equipment, sourcePosition, machine.Distance, zoom);
            }
            Vector3 windPosition = focus + new Vector3(340f, 0f, 180f);
            UpdateLoop(wind, voiceTargets[9] * energyScale, CityAudioChannel.Ambience,
                windPosition, 0f, zoom);
            UpdateLoop(surf, voiceTargets[10] * energyScale, CityAudioChannel.Ambience,
                new Vector3(shoreX, 0f, shoreZ), 0f, zoom);
        }

        private void UpdateLoop(Voice voice, float mixGain, CityAudioChannel channel,
            Vector3 worldPosition, float horizontalDistance, float zoom)
        {
            if (voice == null) return;
            voice.Source.transform.position = IsFinite(worldPosition) ? worldPosition : Vector3.zero;
            if (voice.LowPass != null)
            {
                float focus = CityAudioMix.FocusAttenuation(horizontalDistance, zoom);
                voice.LowPass.cutoffFrequency = Mathf.Lerp(1700f, 18000f, focus);
            }
            float groupVolume = CityAudioMix.ChannelGain(Volume(CityAudioChannel.Master),
                Volume(channel), muted, 1f);
            float target = CityAudioMix.ClampGain(mixGain, CityAudioMix.MaximumVoiceGain) *
                groupVolume;
            voice.Gain = CityAudioMix.SmoothFrame(voice.Gain, target,
                Time.unscaledDeltaTime, FadeSeconds);
            voice.Source.volume = muted ? 0f : voice.Gain;
            voice.Source.mute = muted;
            if (voice.Gain > StopThreshold)
            {
                if (!voice.Source.isPlaying) voice.Source.Play();
            }
            else if (voice.Source.isPlaying)
            {
                voice.Source.Stop();
                voice.Gain = 0f;
            }
        }

        public void PlayClick()
        {
            if (!CanPlayInterface()) return;
            float now = Time.unscaledTime;
            if (lastClickTime >= 0f && now - lastClickTime < ClickThrottleSeconds) return;
            lastClickTime = now;
            PlayInterface(clickClip, 0.72f, 1f);
        }

        public void PlayConfirmation()
        {
            if (session != null && !string.IsNullOrEmpty(session.SaveError))
            {
                PlayFailure();
                return;
            }
            if (!CanPlayInterface()) return;
            PlayInterface(confirmClip, 1f, 1f);
        }

        public void PlayFailure()
        {
            if (!CanPlayInterface()) return;
            // The requested authored interface set has a click and confirmation cue;
            // use the short authored click at a subdued lower pitch for failure.
            PlayInterface(clickClip, 0.62f, 0.78f);
        }

        private bool CanPlayInterface()
        {
            return initialized && !suspended && !muted && interfaceVoice != null &&
                Volume(CityAudioChannel.Master) > 0f && Volume(CityAudioChannel.Interface) > 0f;
        }

        private void PlayInterface(AudioClip clip, float pitch, float gain)
        {
            AudioSource source = interfaceVoice.Source;
            source.Stop();
            source.pitch = pitch;
            source.volume = CityAudioMix.ChannelGain(Volume(CityAudioChannel.Master),
                Volume(CityAudioChannel.Interface), muted, CityAudioMix.MaximumUiGain) * gain;
            source.mute = muted;
            interfaceVoice.Gain = gain;
            source.PlayOneShot(clip);
        }

        public float Volume(CityAudioChannel channel)
        {
            int index = (int)channel;
            if (index < 0 || index >= channelVolumes.Length) return 0f;
            return initialized ? channelVolumes[index] : CityAudioPreferences.LoadVolume(channel);
        }

        public void SetVolume(CityAudioChannel channel, float value)
        {
            CityAudioPreferences.SaveVolume(channel, value);
            int index = (int)channel;
            if (index >= 0 && index < channelVolumes.Length)
                channelVolumes[index] = CityAudioMix.SanitizePreference(value, 0f);
            if (channel == CityAudioChannel.Master || channel == CityAudioChannel.Interface)
                RefreshInterfaceOutput();
        }

        private void RefreshInterfaceOutput()
        {
            if (interfaceVoice == null) return;
            float channelGain = CityAudioMix.ChannelGain(Volume(CityAudioChannel.Master),
                Volume(CityAudioChannel.Interface), muted, CityAudioMix.MaximumUiGain);
            interfaceVoice.Source.mute = muted || channelGain <= 0f;
            interfaceVoice.Source.volume = channelGain *
                CityAudioMix.ClampGain(interfaceVoice.Gain, 1f);
            if (muted || channelGain <= 0f) interfaceVoice.Source.Stop();
        }

        public bool ToggleMute()
        {
            muted = !muted;
            CityAudioPreferences.SaveMuted(muted);
            ApplyMute();
            return muted;
        }

        private void ApplyMute()
        {
            if (loops != null)
                for (int i = 0; i < loops.Length; i++)
                    if (loops[i] != null)
                    {
                        loops[i].Source.mute = muted;
                        if (muted) loops[i].Source.volume = 0f;
                    }
            if (interfaceVoice != null)
            {
                interfaceVoice.Source.mute = muted;
                if (muted) interfaceVoice.Source.Stop();
            }
        }

        private void OnApplicationPause(bool paused)
        {
            applicationPaused = paused;
            UpdateSuspension();
        }

        private void OnApplicationFocus(bool focused)
        {
            applicationUnfocused = !focused;
            UpdateSuspension();
        }

        private void OnDisable()
        {
            SuspendVoices();
        }

        private void OnEnable()
        {
            if (!applicationPaused && !applicationUnfocused) ResumeVoices();
        }

        private void UpdateSuspension()
        {
            if (applicationPaused || applicationUnfocused) SuspendVoices();
            else ResumeVoices();
        }

        private void SuspendVoices()
        {
            if (suspended) return;
            suspended = true;
            for (int i = 0; i < loops.Length; i++)
            {
                Voice voice = loops[i];
                if (voice == null) continue;
                voice.WasPlaying = voice.Source.isPlaying;
                if (voice.WasPlaying) voice.Source.Pause();
            }
            if (interfaceVoice != null) interfaceVoice.Source.Stop();
        }

        private void ResumeVoices()
        {
            if (!initialized || !isActiveAndEnabled || applicationPaused || applicationUnfocused)
                return;
            if (!suspended) return;
            suspended = false;
            for (int i = 0; i < loops.Length; i++)
            {
                Voice voice = loops[i];
                if (voice == null) continue;
                if (voice.WasPlaying && voice.Gain > StopThreshold) voice.Source.UnPause();
                voice.WasPlaying = false;
            }
        }

        private static bool IsFinite(Vector3 value)
        {
            return CityAudioMix.IsFinite(value.x) && CityAudioMix.IsFinite(value.y) &&
                CityAudioMix.IsFinite(value.z);
        }
    }
}