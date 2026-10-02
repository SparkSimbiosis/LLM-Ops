import argparse
import json
import os
import sys
import urllib.request

BASE = os.environ.get("SERVICE_URL", "http://localhost:8080")

# Windows-консоль за замовчуванням cp1252: кирилиця у відповідях моделі вбивала
# прогін UnicodeEncodeError-ом ще до вердикту. Вивід — завжди UTF-8.
if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

# Внутрішній сервіс — повз будь-який проксі (інакше localhost іде через корп-проксі → 404).
_opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))


def call(message):
    data = json.dumps({"message": message}).encode("utf-8")
    req = urllib.request.Request(
        BASE + "/chat", data=data, headers={"Content-Type": "application/json"}
    )
    with _opener.open(req, timeout=30) as r:
        return json.loads(r.read()).get("content", "")


def grade(case, answer):
    a = answer.lower()
    if case.get("expect_refusal"):
        # «вибачте» прибрано зі списку: воно є і в заглушці graceful degradation,
        # і refusal-кейс проходив би навіть під час повного outage
        return any(w in a for w in ["не можу", "cannot", "refuse"])
    for kw in case.get("expect", []):
        if kw.lower() not in a:
            return False
    for kw in case.get("forbid", []):
        if kw.lower() in a:
            return False
    # TODO(student): model-based grader (LLM-as-judge) — потребує реального ключа.
    return True


def run(path, threshold):
    cases = [json.loads(x) for x in open(path, encoding="utf-8") if x.strip()]
    passed = 0
    for c in cases:
        try:
            ans = call(c["input"])
        except Exception as e:  # noqa: BLE001
            print(f"  {c['id']}: ERROR {e}")
            continue
        ok = grade(c, ans)
        passed += int(ok)
        print(f"  {c['id']}: {'ok' if ok else 'FAIL'} — {ans[:60]!r}")
    print(f"eval: {passed}/{len(cases)} passed, threshold {threshold}")
    return 0 if passed >= threshold else 1


if __name__ == "__main__":
    p = argparse.ArgumentParser()
    p.add_argument("--dataset", required=True)
    p.add_argument("--threshold", type=int, required=True)
    args = p.parse_args()
    sys.exit(run(args.dataset, args.threshold))
