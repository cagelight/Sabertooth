using System;
using Sabertooth.Lexicon;

namespace Sabertooth {
	class Core {
		public static int Main (string[] args) {
			Console.WriteLine ("Welcome to Project Sabertooth.");

			HTTPResponse testResponse = new HTTPResponse(HTTPResponse.Code.N200, MIME.Plaintext);
			testResponse.AddInstruction (HTTPResponse.Instruction.SetCookie (new Statement("L1","DDDFFF"), ".snsys.us"));
			testResponse.AddInstruction (HTTPResponse.Instruction.SetCookie (new Statement("L2c", "DFHFVSD"), ".snsys.us", "/subdir"));
			Console.WriteLine (testResponse);

			return 0;
		}
	}
}
