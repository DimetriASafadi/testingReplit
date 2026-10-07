using System;
using System.Reflection;
using NewGaza;
using UnityEngine;

internal static class EquipmentTrackSteeringChecks
{
    private const float VehicleScale = .07f;
    private static int assertions;

    internal static int Run()
    {
        assertions = 0;
        CheckDifferentialTrackTravel();
        CheckTruckSteeringAndWheelRoll();
        return assertions;
    }

    private static void CheckDifferentialTrackTravel()
    {
        using (var geometry = new CityGeometry())
        {
            Material material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            var vehicleObject = new GameObject("Differential track test vehicle");
            vehicleObject.transform.localScale = Vector3.one * VehicleScale;
            Mesh belt = EquipmentGeometry.TrackBelt(geometry, "Differential test belt",
                1.95f, .44f, .34f);
            var rig = new EquipmentTrackRig(geometry, vehicleObject.transform, belt,
                material, material, material, VehicleScale, .57f, 1.95f, .44f, .34f);

            rig.Update();
            vehicleObject.transform.localPosition += new Vector3(0f, 0f, .14f);
            rig.Update();
            float straightLeft = rig.LeftTrackDistanceModel;
            float straightRight = rig.RightTrackDistanceModel;
            Check(straightLeft > 1.9f && straightRight > 1.9f,
                "measured straight travel advances both track loops");
            Check(Math.Abs(straightLeft - straightRight) < .001f,
                "straight travel advances left and right tracks equally");
            Check(Math.Abs(rig.LeftRollerAngleDegrees - rig.RightRollerAngleDegrees) < .01f,
                "straight travel rolls both track roller sets equally");

            rig.Update();
            Check(Math.Abs(rig.LeftTrackDistanceModel - straightLeft) < .0001f &&
                Math.Abs(rig.RightTrackDistanceModel - straightRight) < .0001f,
                "stationary root produces no shoe or roller travel");

            vehicleObject.transform.localRotation = Quaternion.Euler(0f, 12f, 0f);
            rig.Update();
            Check(rig.LeftTrackDistanceModel < straightLeft &&
                rig.RightTrackDistanceModel > straightRight,
                "measured in-place yaw drives the inner and outer tracks in opposite directions");
            Check(rig.LeftRollerAngleDegrees < rig.RightRollerAngleDegrees,
                "each side's rollers follow its signed differential track travel");

            float leftAtTurn = rig.LeftTrackDistanceModel;
            float rightAtTurn = rig.RightTrackDistanceModel;
            rig.Update();
            Check(Math.Abs(rig.LeftTrackDistanceModel - leftAtTurn) < .0001f &&
                Math.Abs(rig.RightTrackDistanceModel - rightAtTurn) < .0001f,
                "parked root yaw stop leaves both track loops still");

            Transform turret = new GameObject("Independent idle slewing turret").transform;
            turret.SetParent(vehicleObject.transform, false);
            turret.localRotation = Quaternion.Euler(0f, 90f, 0f);
            rig.Update();
            Check(Math.Abs(rig.LeftTrackDistanceModel - leftAtTurn) < .0001f &&
                Math.Abs(rig.RightTrackDistanceModel - rightAtTurn) < .0001f,
                "independent turret slew never spins stationary root tracks");

            UnityEngine.Object.Destroy(material);
            UnityEngine.Object.Destroy(vehicleObject);
        }
    }

    private static void CheckTruckSteeringAndWheelRoll()
    {
        using (var geometry = new CityGeometry())
        {
            Material material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            var fleetObject = new GameObject("Truck steering test fleet");
            CityFleet fleet = fleetObject.AddComponent<CityFleet>();
            fleet.Initialize(geometry, material, material, material, material, material, material);
            Transform truck = (Transform)GetField(fleet, "truck");
            Transform[] wheels = (Transform[])GetField(fleet, "wheels");
            SetField(fleet, "truckOwned", true);
            truck.gameObject.SetActive(true);

            Vector3[] originalWheelPositions = new Vector3[wheels.Length];
            for (int i = 0; i < wheels.Length; i++)
                originalWheelPositions[i] = wheels[i].position;

            InvokePrivate(fleet, "UpdateTruckSteering", 0f);
            Transform[] pivots = (Transform[])GetField(fleet, "truckSteeringPivots");
            Check(pivots != null && pivots.Length == 2,
                "truck receives separate left/right front steering knuckles");
            Check(wheels[0].parent == pivots[0] && wheels[1].parent == pivots[1],
                "only the front wheel roots are parented to steering knuckles");
            for (int i = 0; i < wheels.Length; i++)
                Check(Vector3.Distance(originalWheelPositions[i], wheels[i].position) < .0001f,
                    "steering pivot installation preserves authored wheel contact positions");
            Check(wheels[2].parent == truck && wheels[3].parent == truck &&
                wheels[4].parent == truck && wheels[5].parent == truck,
                "rear wheel roots retain their unsteered chassis parents");

            Vector3 previousPosition = truck.localPosition;
            SetField(fleet, "previousWheelPosition", previousPosition);
            SetField(fleet, "haveMotionSample", true);
            truck.localPosition += new Vector3(0f, 0f, .14f);
            truck.localRotation = Quaternion.Euler(0f, 12f, 0f);
            SetField(fleet, "truckTravelAdvancedThisFrame", true);
            InvokePrivate(fleet, "UpdateTruckSteering", .1f);
            float steeringAngle = (float)GetProperty(fleet, "TruckSteeringAngleDegrees");
            Check(steeringAngle > 0f && steeringAngle <= 28f,
                "front steering follows measured heading curvature and clamps to the road-wheel limit");
            Check(Math.Abs(steeringAngle) > .1f,
                "real route turn produces a visible smoothed front steering angle");

            InvokePrivate(fleet, "UpdateWheelRoll", .1f);
            Check(Quaternion.Angle(wheels[0].localRotation, Quaternion.identity) > .1f &&
                Quaternion.Angle(wheels[2].localRotation, Quaternion.identity) > .1f,
                "front and rear tyres keep their actual-distance rolling animation while steering");

            Vector3 frontAxle = wheels[0].rotation * Vector3.right;
            Vector3 frontForward = wheels[0].rotation * Vector3.forward;
            Vector3 rearAxle = wheels[2].rotation * Vector3.right;
            Vector3 truckRight = truck.rotation * Vector3.right;
            Vector3 truckForward = truck.rotation * Vector3.forward;
            Check(Math.Abs(Vector3.Dot(frontAxle, frontForward)) < .001f,
                "steered front axle remains orthogonal to the rolling tyre direction");
            Check(Vector3.Distance(frontAxle, truckRight) > .05f,
                "front axle visibly yaws relative to the chassis during an actual turn");
            Check(Vector3.Dot(rearAxle, truckRight) > .999f &&
                Math.Abs(Vector3.Dot(rearAxle, truckForward)) < .001f,
                "rear axle remains square to the chassis with no steering articulation");

            SetField(fleet, "truckTravelAdvancedThisFrame", false);
            truck.localPosition += new Vector3(0f, 0f, .15f);
            float frontRollBeforeParkedMove = Quaternion.Angle(
                wheels[0].localRotation, Quaternion.identity);
            InvokePrivate(fleet, "UpdateWheelRoll", .1f);
            Check(Math.Abs(Quaternion.Angle(wheels[0].localRotation, Quaternion.identity) -
                frontRollBeforeParkedMove) < .001f,
                "parked wheels stop rolling when measured travel is gated off");

            InvokePrivate(fleet, "OnDestroy");
            UnityEngine.Object.Destroy(fleetObject);
            UnityEngine.Object.Destroy(material);
        }
    }

    private static object GetField(object target, string name)
    {
        FieldInfo field = target.GetType().GetField(name,
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (field == null) throw new InvalidOperationException("Missing production field: " + name);
        return field.GetValue(target);
    }

    private static object GetProperty(object target, string name)
    {
        PropertyInfo property = target.GetType().GetProperty(name,
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (property == null) throw new InvalidOperationException("Missing production property: " + name);
        return property.GetValue(target, null);
    }

    private static void SetField(object target, string name, object value)
    {
        FieldInfo field = target.GetType().GetField(name,
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (field == null) throw new InvalidOperationException("Missing production field: " + name);
        field.SetValue(target, value);
    }

    private static void InvokePrivate(object target, string name, params object[] arguments)
    {
        MethodInfo method = target.GetType().GetMethod(name,
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (method == null) throw new InvalidOperationException("Missing production method: " + name);
        method.Invoke(target, arguments);
    }

    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException("Equipment track/steering check failed: " + message);
    }
}