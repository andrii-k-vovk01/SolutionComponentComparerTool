using System;

namespace SolutionComponentComparer.Tests
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            int failures = EngineTests.Run(args);
            Console.WriteLine();
            failures += RepositoryTests.Run();
            return failures == 0 ? 0 : 1;
        }
    }
}
