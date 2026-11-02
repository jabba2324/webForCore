<%@ Page Language="C#" %>
<%-- The application's static state, new in each worker process (#56), and the process. --%>
domain=<%= DomainState.Id %>;starts=<%= DomainState.ApplicationStarts %>;process=<%= System.Environment.ProcessId %>
