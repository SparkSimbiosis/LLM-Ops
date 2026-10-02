# Eval runner (skeleton)

Дається skeleton (`run.py`, `golden.jsonl`, `requirements.txt`). Студент добудовує:

- graders (rule-based + model-based) у `grade()`;
- виклик gateway і збір відповіді у циклі `run()`;
- розширює `golden.jsonl` репрезентативними кейсами;
- пороги під свій сценарій.

CI (`.github/workflows/eval-gate.yml`) запускає `run.py` на pull request і блокує regression.
Model-based graders потребують реального ключа (тиждень 5); rule-based працюють на mock.
