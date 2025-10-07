Instructions:
1) Delete/replace any previous 'CelestialProjector.cs' that had a multi-argument constructor.
2) Add these three files under 'Cameras/Projection':
   - CelestialProjector.cs        (implements ICelestialProjector with Create(...))
   - ProjectionMath.cs            (math helpers; independent)
   - BoresightProjector.cs        (optional utility; NOT used by DI)
3) Ensure Program.cs DI registration is:
   services.AddSingleton<ICelestialProjector, CelestialProjector>();
4) StarFieldEngine should call the ICelestialProjector.Create(settings, utc) pattern, not new CelestialProjector(...).
