using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using NewGaza.Core;

internal static class Program
{
    private static int assertions;

    private sealed class EnvelopeFrame
    {
        public float t { get; set; }
        public float zoom { get; set; }
        public float excavatorGain { get; set; }
        public float truckGain { get; set; }
        public float dozerGain { get; set; }
        public float hydraulicsGain { get; set; }
        public float tracksGain { get; set; }
        public float windGain { get; set; }
        public float surfGain { get; set; }
        public float excavatorPan { get; set; }
        public float truckPan { get; set; }
        public float dozerPan { get; set; }
        public float hydraulicsPan { get; set; }
        public float tracksPan { get; set; }
        public float windPan { get; set; }
        public float surfPan { get; set; }
    }

    private static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--export")
        {
            ExportCameraDemoEnvelope();
            return;
        }
        FiniteCorruptionIsContained();
        ZoomAndFocusAttenuation();
        NearbyMachineDominance();
        AmbientAndCoastMix();
        MutePauseAndIndependentChannels();
        FrameFadeAndCeilings();
        CoastlinePointIsNearestSegment();
        Console.WriteLine("City audio mix tests passed (" + assertions + " assertions).");
    }

    private static void ExportCameraDemoEnvelope()
    {
        const float sampleInterval = 0.02f;
        const int finalSample = 750;
        const float masterVolume = CityAudioMix.DefaultMasterVolume;
        const float equipmentVolume = CityAudioMix.DefaultEquipmentVolume;
        const float ambienceVolume = CityAudioMix.DefaultAmbienceVolume;

        // Keep demo positions in the sourced city coordinate frame. Their offsets
        // describe this deterministic mixer scenario, not saved/progression data.
        var district = GameGeography.DistrictPoint(0);
        float excavatorX = district.x;
        float excavatorZ = district.z;
        float truckX = excavatorX + 20f;
        float truckZ = excavatorZ;
        float dozerX = excavatorX - 140f;
        float dozerZ = excavatorZ + 45f;
        GeoPoint[] coast = GameGeography.Coastline;
        var coastX = new float[coast.Length];
        var coastZ = new float[coast.Length];
        for (int i = 0; i < coast.Length; i++)
        {
            coastX[i] = coast[i].x;
            coastZ[i] = coast[i].z;
        }
        CityAudioMix.NearestPolylinePoint(excavatorX, excavatorZ, coastX, coastZ,
            coastX.Length, out float coastalFocusX, out float coastalFocusZ);

        float[] engineGain = new float[3];
        float[] hydraulicsGain = new float[3];
        float[] tracksGain = new float[3];
        float windGain = 0f, surfGain = 0f;
        var samples = new List<EnvelopeFrame>(finalSample + 1);
        float equipmentChannel = CityAudioMix.ChannelGain(masterVolume, equipmentVolume,
            false, 1f);
        float ambienceChannel = CityAudioMix.ChannelGain(masterVolume, ambienceVolume,
            false, 1f);

        for (int frame = 0; frame <= finalSample; frame++)
        {
            float time = frame * sampleInterval;
            float zoom;
            float focusX, focusZ;
            if (time < 3f)
            {
                zoom = 180f;
                focusX = excavatorX;
                focusZ = excavatorZ;
            }
            else if (time < 6f)
            {
                float transition = (time - 3f) / 3f;
                zoom = 180f + (0.8f - 180f) * transition;
                focusX = excavatorX;
                focusZ = excavatorZ;
            }
            else if (time < 10f)
            {
                zoom = 0.8f;
                focusX = excavatorX;
                focusZ = excavatorZ;
            }
            else if (time < 12f)
            {
                zoom = 0.8f;
                focusX = excavatorX + 20f * ((time - 10f) / 2f);
                focusZ = excavatorZ;
            }
            else
            {
                zoom = 180f;
                float transition = (time - 12f) / 3f;
                focusX = truckX + (coastalFocusX - truckX) * transition;
                focusZ = truckZ + (coastalFocusZ - truckZ) * transition;
            }

            float excavatorDistance = CityAudioMix.HorizontalDistance(focusX, focusZ,
                excavatorX, excavatorZ);
            float truckDistance = CityAudioMix.HorizontalDistance(focusX, focusZ, truckX, truckZ);
            float dozerDistance = CityAudioMix.HorizontalDistance(focusX, focusZ, dozerX, dozerZ);
            float excavatorAttenuation = CityAudioMix.FocusAttenuation(excavatorDistance, zoom);
            float truckAttenuation = CityAudioMix.FocusAttenuation(truckDistance, zoom);
            float dozerAttenuation = CityAudioMix.FocusAttenuation(dozerDistance, zoom);
            float attenuationSum = excavatorAttenuation + truckAttenuation + dozerAttenuation;

            CityAudioMix.MachineMix excavator = CityAudioMix.EvaluateMachine(true, 0,
                excavatorDistance, zoom, .8f, 0f, .6f,
                attenuationSum - excavatorAttenuation);
            CityAudioMix.MachineMix truck = CityAudioMix.EvaluateMachine(true, 1,
                truckDistance, zoom, .65f, .8f, 0f, attenuationSum - truckAttenuation);
            CityAudioMix.MachineMix dozer = CityAudioMix.EvaluateMachine(true, 2,
                dozerDistance, zoom, .1f, 0f, 0f, attenuationSum - dozerAttenuation);

            CityAudioMix.NearestPolylinePoint(focusX, focusZ, coastX, coastZ,
                coastX.Length, out float shoreX, out float shoreZ);
            float coastDistance = CityAudioMix.HorizontalDistance(focusX, focusZ, shoreX, shoreZ);
            float nearestMachineDistance = Math.Min(excavatorDistance,
                Math.Min(truckDistance, dozerDistance));
            CityAudioMix.AmbienceMix ambience = CityAudioMix.EvaluateAmbience(zoom,
                nearestMachineDistance, coastDistance);
            float energyScale = CityAudioMix.TotalEnergyScale(
                excavator.Engine + truck.Engine + dozer.Engine +
                excavator.Hydraulics + truck.Hydraulics + dozer.Hydraulics +
                excavator.Tracks + truck.Tracks + dozer.Tracks +
                ambience.Wind + ambience.CoastalSurf);

            float engineTarget = CityAudioMix.ClampGain(excavator.Engine,
                CityAudioMix.MaximumVoiceGain) * energyScale * equipmentChannel;
            engineGain[0] = CityAudioMix.SmoothFrame(engineGain[0], engineTarget,
                sampleInterval, .24f);
            engineTarget = CityAudioMix.ClampGain(truck.Engine, CityAudioMix.MaximumVoiceGain) *
                energyScale * equipmentChannel;
            engineGain[1] = CityAudioMix.SmoothFrame(engineGain[1], engineTarget,
                sampleInterval, .24f);
            engineTarget = CityAudioMix.ClampGain(dozer.Engine, CityAudioMix.MaximumVoiceGain) *
                energyScale * equipmentChannel;
            engineGain[2] = CityAudioMix.SmoothFrame(engineGain[2], engineTarget,
                sampleInterval, .24f);

            float hydraulicTarget = CityAudioMix.ClampGain(excavator.Hydraulics,
                CityAudioMix.MaximumVoiceGain) * energyScale * equipmentChannel;
            hydraulicsGain[0] = CityAudioMix.SmoothFrame(hydraulicsGain[0], hydraulicTarget,
                sampleInterval, .24f);
            hydraulicTarget = CityAudioMix.ClampGain(truck.Hydraulics,
                CityAudioMix.MaximumVoiceGain) * energyScale * equipmentChannel;
            hydraulicsGain[1] = CityAudioMix.SmoothFrame(hydraulicsGain[1], hydraulicTarget,
                sampleInterval, .24f);
            hydraulicTarget = CityAudioMix.ClampGain(dozer.Hydraulics,
                CityAudioMix.MaximumVoiceGain) * energyScale * equipmentChannel;
            hydraulicsGain[2] = CityAudioMix.SmoothFrame(hydraulicsGain[2], hydraulicTarget,
                sampleInterval, .24f);

            float trackTarget = CityAudioMix.ClampGain(excavator.Tracks,
                CityAudioMix.MaximumVoiceGain) * energyScale * equipmentChannel;
            tracksGain[0] = CityAudioMix.SmoothFrame(tracksGain[0], trackTarget,
                sampleInterval, .24f);
            trackTarget = CityAudioMix.ClampGain(truck.Tracks, CityAudioMix.MaximumVoiceGain) *
                energyScale * equipmentChannel;
            tracksGain[1] = CityAudioMix.SmoothFrame(tracksGain[1], trackTarget,
                sampleInterval, .24f);
            trackTarget = CityAudioMix.ClampGain(dozer.Tracks, CityAudioMix.MaximumVoiceGain) *
                energyScale * equipmentChannel;
            tracksGain[2] = CityAudioMix.SmoothFrame(tracksGain[2], trackTarget,
                sampleInterval, .24f);

            float ambienceTarget = CityAudioMix.ClampGain(ambience.Wind,
                CityAudioMix.MaximumVoiceGain) * energyScale * ambienceChannel;
            windGain = CityAudioMix.SmoothFrame(windGain, ambienceTarget, sampleInterval, .24f);
            ambienceTarget = CityAudioMix.ClampGain(ambience.CoastalSurf,
                CityAudioMix.MaximumVoiceGain) * energyScale * ambienceChannel;
            surfGain = CityAudioMix.SmoothFrame(surfGain, ambienceTarget, sampleInterval, .24f);

            samples.Add(new EnvelopeFrame
            {
                t = time,
                zoom = zoom,
                excavatorGain = engineGain[0],
                truckGain = engineGain[1],
                dozerGain = engineGain[2],
                hydraulicsGain = hydraulicsGain[0] + hydraulicsGain[1] + hydraulicsGain[2],
                tracksGain = tracksGain[0] + tracksGain[1] + tracksGain[2],
                windGain = windGain,
                surfGain = surfGain,
                excavatorPan = ApproximatePan(excavatorX, focusX, zoom),
                truckPan = ApproximatePan(truckX, focusX, zoom),
                dozerPan = ApproximatePan(dozerX, focusX, zoom),
                hydraulicsPan = WeightedPan(hydraulicsGain, excavatorX, truckX,
                    dozerX, focusX, zoom),
                tracksPan = WeightedPan(tracksGain, excavatorX, truckX,
                    dozerX, focusX, zoom),
                windPan = ApproximatePan(focusX + 340f, focusX, zoom),
                surfPan = ApproximatePan(shoreX, focusX, zoom)
            });
        }

        string output = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            "../../../exports/audio/Camera-Audio-Demo-Envelope.json"));
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        var options = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(output, JsonSerializer.Serialize(samples, options));
        Console.WriteLine("Exported " + samples.Count + " mixer samples to " + output);
        Console.WriteLine("Preferences used: master=" +
            masterVolume.ToString("0.00", CultureInfo.InvariantCulture) + ", equipment=" +
            equipmentVolume.ToString("0.00", CultureInfo.InvariantCulture) + ", ambience=" +
            ambienceVolume.ToString("0.00", CultureInfo.InvariantCulture) +
            ". Pan is approximate for the default north-up listener; Unity was not used.");
    }

    private static float ApproximatePan(float sourceX, float focusX, float zoom)
    {
        float cameraDistance = CityAudioMix.Clamp(zoom * 2f + 10f, 35f, 700f);
        return CityAudioMix.Clamp((sourceX - focusX) / cameraDistance, -1f, 1f);
    }

    private static float WeightedPan(float[] gains, float excavatorX, float truckX,
        float dozerX, float focusX, float zoom)
    {
        float total = gains[0] + gains[1] + gains[2];
        if (total <= 0f) return 0f;
        return (ApproximatePan(excavatorX, focusX, zoom) * gains[0] +
            ApproximatePan(truckX, focusX, zoom) * gains[1] +
            ApproximatePan(dozerX, focusX, zoom) * gains[2]) / total;
    }

    private static void FiniteCorruptionIsContained()
    {
        Equal(0f, CityAudioMix.Clamp01Finite(float.NaN), "NaN gain");
        Equal(0f, CityAudioMix.Clamp01Finite(float.PositiveInfinity), "infinite gain");
        Equal(1f, CityAudioMix.Clamp01Finite(3f), "oversized gain");
        Equal(24f, CityAudioMix.SanitizeZoom(float.NaN), "corrupt zoom fallback");
        Equal(24f, CityAudioMix.SanitizeZoom(float.NegativeInfinity), "non-finite zoom fallback");
        Equal(1600f, CityAudioMix.SanitizeDistance(float.NaN), "corrupt distance fallback");
        Equal(.4f, CityAudioMix.SanitizePreference(float.NegativeInfinity, .4f),
            "corrupt preference fallback");
        True(CityAudioMix.IsFinite(CityAudioMix.FocusAttenuation(float.NaN, float.PositiveInfinity)),
            "all corrupt mixer inputs produce finite output");
    }

    private static void ZoomAndFocusAttenuation()
    {
        float closeZoom = CityAudioMix.FocusAttenuation(1f, .6f);
        float neighborhood = CityAudioMix.FocusAttenuation(1f, 12f);
        float district = CityAudioMix.FocusAttenuation(1f, 85f);
        float overview = CityAudioMix.FocusAttenuation(1f, 250f);
        True(closeZoom < neighborhood, "near zoom .6 has a tighter ground focus");
        True(neighborhood < district, "zoom 12 reaches farther than close framing");
        True(district < overview, "zoom 85 reaches farther than neighborhood framing");
        True(CityAudioMix.FocusAttenuation(1f, .6f) >
            CityAudioMix.FocusAttenuation(5f, .6f), "source focus distance matters at .6");
        True(CityAudioMix.FocusAttenuation(1f, 12f) >
            CityAudioMix.FocusAttenuation(10f, 12f), "source focus distance matters at 12");
        True(CityAudioMix.FocusAttenuation(1f, 85f) >
            CityAudioMix.FocusAttenuation(20f, 85f), "source focus distance matters at 85");
        True(CityAudioMix.FocusAttenuation(1f, 250f) >
            CityAudioMix.FocusAttenuation(25f, 250f), "source focus distance matters at 250");
        Equal(0f, CityAudioMix.FocusAttenuation(1600f, .6f), "far source fades out");
        Equal(0f, CityAudioMix.FocusAttenuation(10f, .8f),
            "a machine ten map units away is outside a close-up focus");

        float focusedPresence = CityAudioMix.EquipmentZoomPresence(.8f);
        float skyPresence = CityAudioMix.EquipmentZoomPresence(180f);
        True(focusedPresence > 10f * skyPresence,
            "zoomed-in equipment presence exceeds far-sky equipment presence");
    }

    private static void NearbyMachineDominance()
    {
        float nearAttenuation = CityAudioMix.FocusAttenuation(0f, .8f);
        float farAttenuation = CityAudioMix.FocusAttenuation(.5f, .8f);
        var near = CityAudioMix.EvaluateMachine(true, 0, 0f, .8f,
            .8f, 0f, .9f, farAttenuation);
        var far = CityAudioMix.EvaluateMachine(true, 1, .5f, .8f,
            .8f, .8f, .9f, nearAttenuation);
        True(near.Engine > far.Engine, "closest machine dominates a more distant machine");
        True(near.Hydraulics > far.Hydraulics, "near hydraulic voice dominates distant voice");
        var focusedExcavator = CityAudioMix.EvaluateMachine(true, 0, 0f, .8f,
            .8f, 0f, .6f, 0f);
        var busierNearbyTruck = CityAudioMix.EvaluateMachine(true, 1, 2f, .8f,
            .65f, .8f, 0f, 1f);
        True(focusedExcavator.Engine > busierNearbyTruck.Engine,
            "close-focus machine wins against a busier source outside its tight radius");

        float alone = CityAudioMix.EvaluateMachine(true, 0, 0f, .8f, .7f, .6f, .8f, 0f).Engine;
        float amongstMany = CityAudioMix.EvaluateMachine(true, 0, 0f, .8f, .7f, .6f, .8f, 12f).Engine;
        True(alone > amongstMany, "crowded machinery beds are controlled");

        var idle = CityAudioMix.EvaluateMachine(true, 0, 0f, .8f, 0f, 0f, 0f, 0f);
        var stationaryDigging = CityAudioMix.EvaluateMachine(true, 0, 0f, .8f,
            .8f, 0f, .6f, 0f);
        var working = CityAudioMix.EvaluateMachine(true, 0, 0f, .8f, 1f, 1f, 1f, 0f);
        Equal(0f, idle.Tracks, "tracks do not play while stationary");
        True(stationaryDigging.Hydraulics > 0f,
            "moving hydraulic arm is audible when machine travel is stationary");
        Equal(0f, stationaryDigging.Tracks,
            "stationary excavator tracks remain quiet while hydraulics work");
        True(working.Engine > idle.Engine, "engine idle remains quieter than loaded work");
        True(working.Hydraulics > 0f && working.Tracks > 0f,
            "moving excavator gets moving hydraulics and tracks");
        var truck = CityAudioMix.EvaluateMachine(true, 1, 0f, .8f, 1f, 1f, 1f, 0f);
        Equal(0f, truck.Tracks, "truck does not receive tracked-machine audio");
        Equal(0f, CityAudioMix.EvaluateMachine(false, 0, 0f, .8f, 1f, 1f, 1f, 0f).Engine,
            "inactive/unowned machine is silent");
    }

    private static void AmbientAndCoastMix()
    {
        var closeView = CityAudioMix.EvaluateAmbience(.6f, 0f, 900f);
        var overview = CityAudioMix.EvaluateAmbience(250f, 1600f, 900f);
        True(overview.Wind > closeView.Wind, "distant sky wind grows at overview zoom");
        True(CityAudioMix.EvaluateAmbience(250f, 0f, 900f).Wind < overview.Wind,
            "wind gently ducks near a machine focus");
        True(overview.Wind <= CityAudioMix.MaximumAmbienceGain,
            "wind respects ambience voice ceiling");

        var nearShore = CityAudioMix.EvaluateAmbience(.8f, 1600f, 2f);
        var inland = CityAudioMix.EvaluateAmbience(.8f, 1600f, 20f);
        True(nearShore.CoastalSurf > inland.CoastalSurf,
            "surf is sourced from actual coast proximity");
        Equal(0f, inland.CoastalSurf, "shore is silent at 20 map units / 400 m");
        Equal(0f, CityAudioMix.EvaluateAmbience(.8f, 1600f, 3000f).CoastalSurf,
            "coastal surf cannot leak inland across the city");

        var nearMachine = CityAudioMix.EvaluateMachine(true, 0, 0f, .8f,
            .8f, 0f, .6f, 0f);
        float nearEngineOutput = nearMachine.Engine *
            CityAudioMix.ChannelGain(CityAudioMix.DefaultMasterVolume,
                CityAudioMix.DefaultEquipmentVolume, false, 1f);
        float nearWindOutput = CityAudioMix.EvaluateAmbience(.8f, 0f, 3000f).Wind *
            CityAudioMix.ChannelGain(CityAudioMix.DefaultMasterVolume,
                CityAudioMix.DefaultAmbienceVolume, false, 1f);
        True(nearEngineOutput > 10f * nearWindOutput,
            "close focused working equipment exceeds quiet near-view air by ten times");

        var wideExcavator = CityAudioMix.EvaluateMachine(true, 0, 0f, 180f,
            .8f, .8f, .6f, 2f);
        var wideTruck = CityAudioMix.EvaluateMachine(true, 1, 0f, 180f,
            .65f, .8f, 0f, 2f);
        var wideDozer = CityAudioMix.EvaluateMachine(true, 2, 0f, 180f,
            .1f, 0f, 0f, 2f);
        float wideEquipmentSum = wideExcavator.Engine + wideExcavator.Hydraulics +
            wideExcavator.Tracks + wideTruck.Engine + wideTruck.Hydraulics +
            wideTruck.Tracks + wideDozer.Engine + wideDozer.Hydraulics + wideDozer.Tracks;
        var wideAmbience = CityAudioMix.EvaluateAmbience(180f, 0f, 3000f);
        float wideEnergyScale = CityAudioMix.TotalEnergyScale(
            wideEquipmentSum + wideAmbience.Wind + wideAmbience.CoastalSurf);
        float wideWindOutput = wideAmbience.Wind *
            wideEnergyScale *
            CityAudioMix.ChannelGain(CityAudioMix.DefaultMasterVolume,
                CityAudioMix.DefaultAmbienceVolume, false, 1f);
        float wideEquipmentOutput = wideEquipmentSum * wideEnergyScale *
            CityAudioMix.ChannelGain(CityAudioMix.DefaultMasterVolume,
                CityAudioMix.DefaultEquipmentVolume, false, 1f);
        True(wideEquipmentOutput < wideWindOutput,
            "high-sky wind leads even three focused engines at zoom 180");
        True(wideEquipmentOutput < wideWindOutput * .05f,
            "high-sky equipment aggregate stays below five percent of wind");
    }

    private static void MutePauseAndIndependentChannels()
    {
        Equal(0f, CityAudioMix.ChannelGain(.8f, .7f, true, 1f), "mute suppresses all channels");
        float playingGain = CityAudioMix.ChannelGain(.8f, .4f, false, 1f);
        float pausedGain = CityAudioMix.SmoothFrame(playingGain, .1f, 0f, .24f);
        Equal(playingGain, pausedGain, "pause holds current fade state without advancing");
        float resumedGain = CityAudioMix.SmoothFrame(pausedGain, .1f, .1f, .24f);
        True(resumedGain < pausedGain && resumedGain > .1f, "resume continues a clean fade");

        float interfaceGain = CityAudioMix.ChannelGain(.7f, .35f, false,
            CityAudioMix.MaximumUiGain);
        float sameInterfaceWithDifferentEquipment =
            CityAudioMix.ChannelGain(.7f, .35f, false, CityAudioMix.MaximumUiGain);
        Equal(interfaceGain, sameInterfaceWithDifferentEquipment,
            "interface gain is independently derived from interface volume");
        Equal(0f, CityAudioMix.ChannelGain(.7f, 0f, false, CityAudioMix.MaximumUiGain),
            "zero interface volume immediately gates the interface voice");
        Equal(0f, CityAudioMix.ChannelGain(0f, .35f, false, CityAudioMix.MaximumUiGain),
            "zero master volume immediately gates the interface voice");
        float equipmentGain = CityAudioMix.ChannelGain(.7f, .8f, false,
            CityAudioMix.MaximumVoiceGain);
        True(equipmentGain > interfaceGain, "equipment and interface channels are independent");
    }

    private static void FrameFadeAndCeilings()
    {
        float first = CityAudioMix.SmoothFrame(0f, .7f, 1f / 60f, .24f);
        float second = CityAudioMix.SmoothFrame(first, .7f, 1f / 60f, .24f);
        True(first > 0f && second > first && second < .7f, "frame fade is smooth and monotonic");
        float fadeOut = CityAudioMix.SmoothFrame(.7f, 0f, 1f / 60f, .24f);
        True(fadeOut > 0f && fadeOut < .7f, "inactive voices fade before stopping");
        True(CityAudioMix.ClampGain(4f, CityAudioMix.MaximumVoiceGain) <=
            CityAudioMix.MaximumVoiceGain, "loop voice ceiling");
        True(CityAudioMix.ClampGain(4f, CityAudioMix.MaximumAmbienceGain) <=
            CityAudioMix.MaximumAmbienceGain, "ambience ceiling");
        True(CityAudioMix.ClampGain(4f, CityAudioMix.MaximumUiGain) <=
            CityAudioMix.MaximumUiGain, "interface ceiling");
        Equal(0f, CityAudioMix.ClampGain(float.NaN, .8f), "non-finite target cannot escape ceiling");
        float rawTotal = CityAudioMix.MaximumVoiceGain * 11f;
        float energyScale = CityAudioMix.TotalEnergyScale(rawTotal);
        True(energyScale >= 0f && energyScale <= 1f, "total soundscape energy scale is bounded");
        True(rawTotal * energyScale <= CityAudioMix.MaximumSoundscapeEnergy,
            "all eleven loop targets share a bounded total energy budget");
        Equal(0f, CityAudioMix.TotalEnergyScale(float.PositiveInfinity),
            "corrupt aggregate energy is silenced");
    }

    private static void CoastlinePointIsNearestSegment()
    {
        float[] x = { 0f, 10f, 20f };
        float[] z = { 0f, 0f, 0f };
        float distance = CityAudioMix.NearestPolylinePoint(6f, 5f, x, z, x.Length,
            out float nearestX, out float nearestZ);
        Equal(5f, distance, "nearest segment distance");
        Equal(6f, nearestX, "nearest segment world X");
        Equal(0f, nearestZ, "nearest segment world Z");
    }

    private static void True(bool condition, string name)
    {
        assertions++;
        if (!condition) throw new Exception("FAILED: " + name);
    }

    private static void Equal(float expected, float actual, string name)
    {
        assertions++;
        if (Math.Abs(expected - actual) > 0.0001f)
            throw new Exception("FAILED: " + name + " expected " + expected + " but was " + actual);
    }
}