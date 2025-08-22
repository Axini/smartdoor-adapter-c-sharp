namespace SmartdoorAdapter.Tests.Util
{
    public static class TaskUtil
    {
        // ML move to async
        public static void WaitUntil(Func<bool> predicate, int maxIterations = 20, int timeStep = 100)
        {
            var iteration = 0;

            while (!predicate() && iteration < maxIterations)
            {
                Task.Delay(timeStep).Wait();
                iteration++;
            }
        }

        public static async Task WaitUntilIsTrue(Func<bool> predicate, int timeStep = 100)
        {
            while (!predicate())
            {
                await Task.Delay(timeStep);
            }
        }
    }
}