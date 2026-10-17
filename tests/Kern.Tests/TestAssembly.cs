using Xunit;

// Tests exercise process-wide application state (AppCfgSvc, AppState and the current DB connection).
// Running them concurrently allows one test to change another test's active company or lock its SQLite file.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
