using System;
using System.Text;
using Sabertooth.Lexicon;

namespace Sabertooth {
	class Core {
		public static int Main (string[] args) {
			Console.WriteLine ("Welcome to Project Sabertooth.");
			Server SRV = new Server ();
			SRV.Start ();

			while(true) {
				string command = Console.ReadLine ();
				switch(command) {
				case "quit":
					SRV.Stop ();
					return 0;
				default:
					break;
				}
			}
		}
	}
}
