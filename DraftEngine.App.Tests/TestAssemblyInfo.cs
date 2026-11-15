using Xunit;

// WPF allows a single Application per process; serialize the UI page-open tests so they
// cannot race the rest of the suite or each other.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
