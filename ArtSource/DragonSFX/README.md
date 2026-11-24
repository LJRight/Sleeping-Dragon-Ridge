# 드래곤 효과음

제작일: 2026-11-17

사용자 요청에 따라 유료 서비스 없이 로컬 코드 합성으로 효과음 4종을 제작했다. 외부 음원, 동물 녹음, 유료 생성 API, 다운로드한 사운드 팩은 사용하지 않았다. AI 어시스턴트가 작성한 DSP 코드로 파형을 생성했으며, 전용 생성형 오디오 모델로 만든 결과는 아니다.

## 파일

| 용도 | 파일 | 길이 | 설계 |
|---|---|---:|---|
| 피격 | `wav/dragon_hurt.wav` | 0.80초 | 짧은 고음 상승 뒤 낮아지는 고통의 울음 |
| 사망 | `wav/dragon_death.wav` | 1.48초 | 힘이 빠지는 울음, 마지막 숨, 낮은 충격. 현재 1.5초 풀 반환 이전에 종료 |
| 깨물기 | `wav/dragon_bite.wav` | 1.00초 | 으르렁거림, 돌진 숨소리, 약 0.5초의 턱 닫힘 |
| 최초 발견 | `wav/dragon_alert_roar.wav` | 2.32초 | 들숨, 강한 포효, 낮아지는 잔음. 현재 Scream 길이에 맞춤 |

WAV는 모두 **48,000 Hz / 16-bit PCM / mono**이며 합계 537,776바이트다. 게임에서 위치 기반 재생에 사용할 수 있는 단발 음원이다. `dragon_preview.mp3`와 `dragon_preview.wav`는 피격 → 사망 → 깨물기 → 최초 발견 순서이며, 사이에 0.65초 무음을 넣었다.

## 제작 방법과 재현

`generate.py`에 고정 난수 시드와 합성 설정이 있다. 불규칙한 성대 펄스, 목구멍 공명, 거친 숨, 저음 진동을 겹치고 소리별 피치·입 벌림·음량 곡선을 적용했다. 짧은 반사음과 시작·끝 페이드, DC/초저음 제거, 피크 조절을 적용했다. 음악이나 대사는 없다.

이번 컴퓨터에 이미 설치돼 있던 Blender의 Python/NumPy 및 FFmpeg를 사용했다. 새 패키지는 설치하지 않았다.

```sh
cd /Users/jinwoo/Documents/Github/NetmarbleNeo
/Applications/Blender.app/Contents/Resources/5.3/python/bin/python3.13 ArtSource/DragonSFX/generate.py
ffmpeg -hide_banner -nostdin -y -i ArtSource/DragonSFX/dragon_preview.wav -codec:a libmp3lame -b:a 192k ArtSource/DragonSFX/dragon_preview.mp3
```

다른 환경에서는 NumPy가 있는 Python으로 `generate.py`를 실행할 수 있다. 실행하면 이 폴더의 동명 WAV와 검증 JSON을 다시 생성한다.

## 실제 확인 결과와 남은 검토

- 생성 WAV를 다시 읽어 채널·샘플레이트·샘플 수·음량을 검사했다. 각 파일의 세부 수치와 SHA-256은 `validation.json`에 있다.
- 4종 모두 클리핑 샘플 0, 시작과 마지막 샘플 0. 피크는 -4~-3 dBFS.
- 깨물기 파일의 최대 진폭 시점: 0.509083초. 모션의 실제 턱 닫힘 프레임을 보고 맞춘 결과는 아니며 문서의 50% 타격 시점을 기준으로 했다.
- FFmpeg에서 7.55초 WAV 미리듣기를 끝까지 디코딩하고 MP3로 변환했다. WAV 전체 true peak는 -3.0 dBFS, 통합 음량은 -13.0 LUFS.
- 기술적 파일 검증만 수행했다. 사용자 청취 평가와 드래곤처럼 들리는지에 대한 판단, 실제 기기에서의 음색 평가는 남아 있다. 합성 특유의 인공적인 질감이 있을 수 있다.
- Unity Assets 추가, 코드·프리팹 연결, 재생 타이밍 연동, 플레이·빌드·실기기 검증은 수행하지 않았다.

향후 연결할 때에는 사망 시 기존 포효를 중단하고, 공격 중 피격 음성 중첩과 몬스터 다수의 재생 음량을 검토해야 한다. 이번에는 해당 게임 로직을 변경하지 않았다.
