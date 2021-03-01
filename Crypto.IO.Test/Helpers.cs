using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Xunit.Abstractions;

namespace Crypto.IO.Test
{
    internal class TestOutputConverter : TextWriter
    {
        class TestSession : IDisposable
        {
            public Action<List<(string value, object[] args)>> _onDispose;
            public TestSession(Action<List<(string value, object[] args)>> onDispose) => _onDispose = onDispose;
            public List<(string value, object[] args)> Messages = new List<(string value, object[] arg)>();
            public void Dispose() => _onDispose(Messages);
        }

        ITestOutputHelper _output;
        TestSession _session;

        public TestOutputConverter(ITestOutputHelper output) => _output = output;
        public override Encoding Encoding => Encoding.Default;
        public override void WriteLine(string message) => _session.Messages.Add((message, null));
        public override void WriteLine(string format, params object[] args) => _session.Messages.Add((format, args));
        public IDisposable Session() => _session = new TestSession(Display);
        void Display(List<(string value, object[] args)> messages)
        {
            foreach (var (value, args) in messages)
                if (args == null) _output.WriteLine(value);
                else _output.WriteLine(value, args);
        }
    }
}
