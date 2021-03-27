using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LiteServer
{
    class Log
    {
        [Obsolete("Use server instance logger or make your own for the filterscript (preferred method is to make your own)")]
        public static void LogToConsole(int flag, string module, string message)
        {
            if (module == null || module.Equals("")) { module = "SERVER"; }
            if (flag == 1)
            {
                Console.ForegroundColor = ConsoleColor.Cyan; Console.WriteLine("[" + DateTime.Now + "] (DEBUG) " + module.ToUpper() + ": " + message);
            }
            else if (flag == 2)
            {
                Console.ForegroundColor = ConsoleColor.Green; Console.WriteLine("[" + DateTime.Now + "] (SUCCESS) " + module.ToUpper() + ": " + message);
            }
            else if (flag == 3)
            {
                Console.ForegroundColor = ConsoleColor.DarkYellow; Console.WriteLine("[" + DateTime.Now + "] (WARNING) " + module.ToUpper() + ": " + message);
            }
            else if (flag == 4)
            {
                Console.ForegroundColor = ConsoleColor.Red; Console.WriteLine("[" + DateTime.Now + "] (ERROR) " + module.ToUpper() + ": " + message);
            }
            else if (flag == 6)
            {
                Console.ForegroundColor = ConsoleColor.Magenta; Console.WriteLine("[" + DateTime.Now + "] " + module.ToUpper() + ": " + message);
            }
            else
            {
                Console.WriteLine("[" + DateTime.Now + "] " + module.ToUpper() + ": " + message);
            }
            Console.ForegroundColor = ConsoleColor.White;
        }

    }
}
