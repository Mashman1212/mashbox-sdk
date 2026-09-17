# Resident grass startup fix

Cause: Cotswold Ridge enables full GPU detail residency, but TryBuildWorldCell still applied the world streaming scheduler's eight-cell-per-frame construction cap. Full residency already bypasses per-layer limits and distance pruning, so the world gate spread its initialization across gameplay frames.

Fix: full resident GPU details bypass the world construction gate too. The first gameplay camera can construct all cells and submit their GPU generation before rendering. Visible-instance budgets, distance density, CPU mesh upload limits, and nonresident streaming limits remain intact. Existing cache invalidation uses the same complete rebuild path.

Validation: compiled the runtime assembly with Unity 6000.4.12f1's C# compiler and project references (compile.log); no compiler errors. Package diff whitespace check passed. Play Mode timing and visual confirmation have not been performed. Startup can take longer because initialization now completes together rather than spreading over gameplay frames.