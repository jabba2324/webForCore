namespace SimpleLib
{
    public static class Greeting
    {
        public static string Text => "hello from SimpleLib " + typeof(Greeting).Assembly.GetName().Version;
    }
}
