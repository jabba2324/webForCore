using System;
using System.Web.UI;

public partial class DefaultPage : Page
{
    protected void Page_Load(object sender, EventArgs e)
    {
        Greeting.Text = Greeter.Greet("precompiled");
    }
}
