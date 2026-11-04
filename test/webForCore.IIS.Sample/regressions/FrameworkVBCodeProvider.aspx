<%@ Page Language="VB" %>
<script runat="server">
    ' Issue #88: a VB page compiled with the <system.codedom> entry naming Microsoft.VisualBasic.VBCodeProvider in
    ' System, Version=2.0.0.0 (the sample's Web.config), as .NET 4.0's project templates wrote it. OptionInfer="true"
    ' is one of its provider options: the inferred local below needs it.
    Protected Sub Page_Load(sender As Object, e As EventArgs)
        Response.ContentType = "text/plain"
        Dim inferred = 40 + 2
        Response.Write("vb page: " & inferred.GetType().Name & " " & inferred & vbLf)
    End Sub
</script>
