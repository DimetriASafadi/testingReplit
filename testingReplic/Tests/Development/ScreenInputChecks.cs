using System;
using NewGaza.Core;

internal static class ScreenInputChecks
{
    internal static void Run(Action<bool, string> check)
    {
        check(ScreenInputValidation.InViewport(100, 100, 0, 0, 1601, 733), "ordinary mouse/touch sample inside camera");
        foreach (float value in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            check(!ScreenInputValidation.InViewport(value, 100, 0, 0, 1601, 733), "invalid pointer x rejected");
            check(!ScreenInputValidation.InViewport(100, value, 0, 0, 1601, 733), "invalid pointer y rejected");
            check(!ScreenInputValidation.InViewport(100, 100, 0, 0, value, 733), "invalid viewport width rejected");
            check(!ScreenInputValidation.InViewport(100, 100, 0, value, 1601, 733), "invalid viewport origin rejected");
            check(!ScreenInputValidation.Finite(value), "invalid scroll/rotation sample rejected");
        }
        check(!ScreenInputValidation.InViewport(float.PositiveInfinity, float.NegativeInfinity, 0, 0, 1601, 733),
            "reported Unity inf/-inf sample rejected before projection");
        check(!ScreenInputValidation.InViewport(-1, 20, 0, 0, 1601, 733), "pointer outside left edge");
        check(!ScreenInputValidation.InViewport(20, -1, 0, 0, 1601, 733), "pointer outside bottom edge");
        check(!ScreenInputValidation.InViewport(1601, 20, 0, 0, 1601, 733), "exclusive right edge");
        check(!ScreenInputValidation.InViewport(20, 733, 0, 0, 1601, 733), "exclusive top edge");
        check(!ScreenInputValidation.InViewport(20, 20, 0, 0, 0, 733), "zero viewport while window resizes");
        check(!ScreenInputValidation.InViewport(20, 20, 0, 0, 1601, -1), "invalid viewport height");
        check(ScreenInputValidation.InViewport(200, 150, 200, 150, 400, 300), "offset camera viewport starts inclusively");
        check(!ScreenInputValidation.InViewport(199, 150, 200, 150, 400, 300), "outside offset viewport");
        check(ScreenInputValidation.InViewport(1599, 731, 0, 0, 1601, 733), "finite sample after focus returns");
    }
}