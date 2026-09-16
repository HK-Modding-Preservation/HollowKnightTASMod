using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HollowKnightTAS.AgentBridge
{
    internal static class Program
    {
        private static async Task<int> Main(string[] args)
        {
            Console.InputEncoding = new UTF8Encoding(false, true);
            Console.OutputEncoding = new UTF8Encoding(false, true);
            Console.Error.WriteLine(
                "HollowKnightTAS AgentBridge MCP stdio starting.");
            try
            {
                await using var server = new McpStdioServer(args);
                await server.RunAsync(CancellationToken.None);
                return 0;
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine(
                    "AgentBridgeFatal:"
                    + exception.GetType().Name);
                return 2;
            }
        }
    }
}
