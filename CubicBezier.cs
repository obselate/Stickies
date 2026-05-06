using System;

namespace Stickies;

/// <summary>
/// Solves cubic-bezier easing y = f(t) for animations.
/// CSS bezier convention: P0=(0,0), P1=(x1,y1), P2=(x2,y2), P3=(1,1).
/// </summary>
internal sealed class CubicBezier
{
    private readonly double _x1, _y1, _x2, _y2;

    public CubicBezier(double x1, double y1, double x2, double y2)
    {
        _x1 = x1; _y1 = y1; _x2 = x2; _y2 = y2;
    }

    /// <summary>Maps progress t∈[0,1] to eased output y∈[0,1].</summary>
    public double Ease(double t)
    {
        if (t <= 0) return 0;
        if (t >= 1) return 1;
        // Solve x(p) = t for parameter p via Newton-Raphson, then return y(p).
        double p = t;
        for (int i = 0; i < 8; i++)
        {
            double x = X(p);
            double dx = DX(p);
            if (Math.Abs(dx) < 1e-9) break;
            p -= (x - t) / dx;
            p = Math.Clamp(p, 0.0, 1.0);
        }
        return Y(p);
    }

    private double X(double p)
    {
        double oneMinus = 1 - p;
        return 3 * oneMinus * oneMinus * p * _x1
             + 3 * oneMinus * p * p * _x2
             + p * p * p;
    }

    private double DX(double p)
    {
        double oneMinus = 1 - p;
        return 3 * oneMinus * oneMinus * _x1
             + 6 * oneMinus * p * (_x2 - _x1)
             + 3 * p * p * (1 - _x2);
    }

    private double Y(double p)
    {
        double oneMinus = 1 - p;
        return 3 * oneMinus * oneMinus * p * _y1
             + 3 * oneMinus * p * p * _y2
             + p * p * p;
    }
}
