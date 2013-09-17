using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

using Sabertooth.Lexicon;

namespace Sabertooth {
	public class ClientRequest {
		public string Type;
		public string Path;
		public string Host;
		public IPAddress IP;
		public readonly TimeTracker RequestTime;
		public ClientRequest() {
			RequestTime = new TimeTracker ();
		}
		public ClientRequest(string type, string path) {
			Type = type;
			Path = path;
			RequestTime = new TimeTracker ();
		}
	}
	public class ClientTranslator {
		TcpClient Client;
		Stream Communications;
		BinaryWriter CommOut;
		public ClientTranslator(TcpClient client) {
			Client = client;
			Communications = new BufferedStream(Client.GetStream ());
			CommOut = new BinaryWriter (Communications, Encoding.UTF8);
		}

		public void Close() {
			CommOut.Flush (); CommOut = null;
			Communications.Flush (); Communications = null;
		}

		public ClientRequest ProcessRequest() {
			try {
				ClientRequest clientRequest = ProcessHeader();
				clientRequest.IP = ((IPEndPoint)Client.Client.RemoteEndPoint).Address;
				return clientRequest;
			} catch (Exception e) {
				Console.WriteLine ("Failed to process client request: " + e);
				return null;
			}
		}

		private ClientRequest ProcessHeader() {
			ClientRequest CR = null;
			List<string> headerList = new List<string> ();
			string line = String.Empty;
			int cb;
			while (true) {
				byte fail = 0x00;
				while(fail < 0x0A) {
					cb = Communications.ReadByte();
					if(CR == null) {
						CR = new ClientRequest ();
					}
					if(cb == '\n')
						break;
					if(cb == '\r')
						continue;
					if(cb == -1) {
						++fail;
						continue;
					}
					line += Convert.ToChar (cb);
				}
				if(line == String.Empty) {
					break;
				} else {
					Console.WriteLine(line);
					headerList.Add(String.Copy(line));
					line = String.Empty;
				}
			}
			string[] header = headerList.ToArray ();
			string[] requestLine = header [0].Split (' ');
			CR.Type = requestLine[0];
			CR.Path = requestLine[1];
			for (int i = 1; i < header.Length; ++i) {
				string[] lineInstruction = header [i].Split(new string[] {": "}, StringSplitOptions.None);
				switch (lineInstruction[0]) {
					case "Host":
					CR.Host = lineInstruction [1];
					break;
					default:
					break;
				}
			}
			return CR;
		}

		public void SendHTTP(HTTPObject H) {
			CommOut.Write (H.GetBytes());
		}
	}
}
