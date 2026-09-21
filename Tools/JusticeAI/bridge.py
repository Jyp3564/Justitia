"""One-shot JSON adapter for the user's existing main.py. No keys in Unity."""
import importlib.util
import json
import os
from pathlib import Path
import sys

sys.dont_write_bytecode = True


def run():
    from dotenv import load_dotenv
    from openai import OpenAI, AuthenticationError, RateLimitError, APITimeoutError, APIConnectionError, APIStatusError

    config = json.loads(Path(__file__).with_name("local.json").read_text(encoding="utf-8-sig"))
    source = Path(config["sourceDirectory"])
    spec = importlib.util.spec_from_file_location("justice_case_source", source / "main.py")
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    load_dotenv(source / ".env", encoding="utf-8-sig")
    key = os.getenv("OPENAI_API_KEY", "").strip()
    if not key or key == "your_api_key_here":
        return {"ok": False, "error": "AI 폴더의 .env에 OPENAI_API_KEY를 설정해 주세요."}
    if "--check" in sys.argv:
        return {"ok": True, "configured": True}
    data = json.loads(sys.stdin.read(4096))
    a, b = data.get("host_keyword"), data.get("guest_keyword")
    if not all(isinstance(x, str) and 1 <= len(x.strip()) <= 40 for x in (a, b)):
        return {"ok": False, "error": "두 키워드는 각각 1~40자로 입력해 주세요."}
    try:
        with OpenAI(api_key=key, timeout=45.0, max_retries=0) as client:
            result = module.generate_case(client, os.getenv("OPENAI_MODEL", "gpt-4.1-mini").strip() or "gpt-4.1-mini", a.strip(), b.strip())
        validated = module.GeneratedCase.model_validate(result).model_dump()
        return {"ok": True, **validated}
    except AuthenticationError:
        return {"ok": False, "error": "AI API 키 인증에 실패했습니다."}
    except RateLimitError:
        return {"ok": False, "error": "AI API 사용 한도 또는 요청 제한에 도달했습니다."}
    except APITimeoutError:
        return {"ok": False, "error": "AI 응답 시간이 초과되었습니다. 다시 시도해 주세요."}
    except APIConnectionError:
        return {"ok": False, "error": "AI 서버에 연결하지 못했습니다."}
    except APIStatusError as error:
        return {"ok": False, "error": f"AI 요청 실패 (HTTP {error.status_code}). 모델과 계정 설정을 확인하세요."}
    except (ValueError, OSError):
        return {"ok": False, "error": "AI 응답 형식 또는 프롬프트 파일을 확인해 주세요."}


if __name__ == "__main__":
    try:
        response = run()
    except Exception:
        response = {"ok": False, "error": "AI 실행 환경을 확인해 주세요. Python 패키지와 원본 폴더가 필요합니다."}
    print(json.dumps(response, ensure_ascii=False), flush=True)
    sys.exit(0 if response.get("ok") else 1)
