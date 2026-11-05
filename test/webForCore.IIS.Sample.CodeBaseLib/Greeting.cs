namespace CodeBaseLib
{
    public static class Greeting
    {
        public static string Text => "hello from CodeBaseLib " + typeof(Greeting).Assembly.GetName().Version;
    }
}
