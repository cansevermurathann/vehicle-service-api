"""Exercise the built API over HTTP without a SQL Server connection."""

import json
import os
from pathlib import Path
from queue import Empty, Queue
import subprocess
import threading
from time import monotonic
import unittest
from urllib.error import HTTPError
from urllib.request import urlopen


class HttpContractTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        dll = Path(__file__).resolve().parents[1] / "bin/Debug/net10.0/VehicleServiceApi.dll"
        env = dict(os.environ, ASPNETCORE_ENVIRONMENT="Development", DOTNET_ENVIRONMENT="Development",
                   ConnectionStrings__ServiceDb="Server=localhost;Database=Unused;Integrated Security=True")
        app = subprocess.Popen(
            ["dotnet", str(dll), "--urls", "http://127.0.0.1:0"],
            env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True,
        )
        lines = Queue()

        def read_output():
            for line in app.stdout:
                lines.put(line)
            lines.put(None)

        reader = threading.Thread(target=read_output, daemon=True)

        def stop_app():
            app.terminate()
            try:
                app.wait(timeout=5)
            except subprocess.TimeoutExpired:
                app.kill()
                app.wait()
            reader.join(timeout=5)
            app.stdout.close()

        cls.addClassCleanup(stop_app)
        reader.start()
        output = []
        deadline = monotonic() + 15
        while True:
            try:
                line = lines.get(timeout=max(0, deadline - monotonic()))
            except Empty:
                line = None
            if line is None or monotonic() >= deadline:
                raise RuntimeError("API did not start. Build it first.\n" + "".join(output))
            output.append(line)
            if "Now listening on:" in line:
                cls.base_url = line.split("Now listening on:", 1)[1].strip()
                return

    def test_list_operation_documents_query_and_responses(self):
        with urlopen(self.base_url + "/openapi/v1.json", timeout=5) as response:
            operation = json.load(response)["paths"]["/service-requests"]["get"]
        self.assertEqual(
            {parameter["name"] for parameter in operation["parameters"]},
            {"vehicleId", "status", "afterId", "limit"},
        )
        self.assertEqual(set(operation["responses"]), {"200", "400"})

    def test_invalid_list_parameters_return_field_errors(self):
        for field, value in [("vehicleId", "0"), ("status", "1"), ("afterId", "-1"),
                             ("limit", "0"), ("limit", "101")]:
            with self.subTest(field=field, value=value):
                with self.assertRaises(HTTPError) as context:
                    urlopen(f"{self.base_url}/service-requests?{field}={value}", timeout=5)
                with context.exception as response:
                    self.assertEqual(response.code, 400)
                    self.assertIn(field, json.load(response)["errors"])
