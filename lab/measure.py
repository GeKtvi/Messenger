"""Measures the lab from each region's edge: python lab/measure.py

Prints the median time of 7 requests and which region answered. Needs only Python.
"""
import json
import statistics
import time
import urllib.error
import urllib.request
import uuid

EDGES = {"eu": 8001, "us": 8002, "asia": 8003}
ACCOUNT = {"email": "measure@example.com", "password": "correct horse battery"}


def call(port, method, path, body=None, token=None):
    request = urllib.request.Request(f"http://127.0.0.1:{port}{path}", method=method)
    data = None
    if body is not None:
        data = json.dumps(body).encode()
        request.add_header("Content-Type", "application/json")
    if token:
        request.add_header("Authorization", f"Bearer {token}")
    start = time.perf_counter()
    with urllib.request.urlopen(request, data, timeout=30) as response:
        content = response.read()
        ms = (time.perf_counter() - start) * 1000
        return ms, response.headers["X-Pingo-Region"], json.loads(content) if content else None


def median(port, make):
    results = [make(port) for _ in range(8)][1:]  # the first one opens connections
    regions = ",".join(sorted({r[1] for r in results}))
    return f"{statistics.median(r[0] for r in results):5.0f} ms ({regions})"


try:
    call(EDGES["eu"], "POST", "/api/auth/register", ACCOUNT)
except urllib.error.HTTPError as error:
    if error.code != 409:
        raise
token = call(EDGES["eu"], "POST", "/api/auth/login", ACCOUNT)[2]["accessToken"]  # one token for all regions

print(f"{'from':6}{'list messages':20}{'send message':20}{'log in':20}")
for region, port in EDGES.items():
    read = median(port, lambda p: call(p, "GET", "/api/messages", token=token))
    write = median(port, lambda p: call(p, "PUT", f"/api/messages/{uuid.uuid4()}", {"text": f"from {region}"}, token))
    login = median(port, lambda p: call(p, "POST", "/api/auth/login", ACCOUNT))
    print(f"{region:6}{read:20}{write:20}{login:20}")
