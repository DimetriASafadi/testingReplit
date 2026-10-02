using System;

internal static class Program
{
    private static void Main()
    {
        int assertions = ConstructionCrewChecks.Run();
        Console.WriteLine("PASS actual CityConstructionCrew / articulated rig source checks: " + assertions + " assertions");
    }
}