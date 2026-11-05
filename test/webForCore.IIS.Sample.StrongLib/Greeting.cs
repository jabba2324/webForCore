namespace StrongLib
{
    public static class Greeting
    {
        public static string Text => "hello from StrongLib " + typeof(Greeting).Assembly.GetName().Version;
    }
}
