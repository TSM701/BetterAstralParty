# 자동 업데이트 배포·최초 helper·보존 정책 — 1.0.0-dev

이 문서는 1.0.0-dev 당시의 설계·검증 기록이며 아래 본문은 당시 상태를 보존합니다. 현재 사용 절차는 [README](../README.md), C# 빌드·패키징 절차는 [BUILD](BUILD.md)를 따릅니다.

This is a historical 1.0.0-dev design and verification record; the original text is preserved below. Follow the [README](../README.md) for current usage and [BUILD](BUILD.md) for C# builds and packaging.

이 문서는 코드 경로와 향후 운영 절차를 설명합니다. 현재 Production trust는 비어 있고 private 인증 provider도 없습니다. 개발 후보, unsigned 요청과 테스트 서명 fixture는 실제 배포·설치 대상이 아닙니다. 개인 키·장기 토큰을 생성하거나 모드에 넣지 않았습니다. 공개 릴리스·태그·업로드·실제 설치는 실행하지 않았습니다.

## 두 단계 오프라인 포장

`tools/BetterAstralParty.UpdateDistribution.csproj`는 .NET 8 개발 도구입니다. `--prepare <payload-root> <new-output-root> <current-version> <release-tag> <Stable|Beta> <release-id> <ZIP-asset-id>`는 정확한 8개 owned 파일과 x64 helper를 읽고, DLL metadata/version 및 채널 조건을 확인한 뒤 deterministic ZIP과 unsigned descriptor 두 개를 새 private 출력 폴더에 만듭니다. 기존 출력은 덮어쓰지 않습니다. release/asset ID는 실제 승인된 private draft 운영 절차에서 확정해야 하며 이번 개발 요청의 fixture ID는 배포에 쓰지 않습니다.

외부의 승인된 서명 작업이 `update.descriptor`와 `helper.descriptor`에 RSA-PSS/SHA-256 서명 envelope를 각각 제공합니다. 도구는 개인 키를 만들거나 읽거나 서명하지 않습니다. envelope는 LF 한 줄 종결, key-id 및 canonical Base64를 강제합니다. `--finalize`는 동일한 context와 `update.signature`, `helper.signature`를 받아 **독립적으로 컴파일된 public trust**로 둘을 검증한 뒤 아래 6개 asset을 새 폴더에 만듭니다. 내려받은 public key나 bundle 내 key를 trust로 채택하지 않습니다. 운영 미설정이면 파일 접근 전에 `NotConfigured`/exit 2입니다.

- `BetterAstralParty-<normalized-version>-update.zip`
- `BetterAstralParty.update.manifest`
- `BetterAstralParty.update.signature`
- `helper.descriptor`, `helper.signature`, `BetterAstralParty-UpdateHelper.exe`

GitHub API asset 이름, repository/channel/platform/release/asset ID, ZIP 크기/hash, exact 8개 파일/hash가 descriptor에 결합됩니다. helper는 별도 `BetterAstralParty.HelperDelivery/v1` purpose로 서명하여 update descriptor와 바꿔 사용할 수 없습니다. `v` 접두 GitHub tag는 실제 tag context를 유지하고 DLL/helper version은 정규화합니다.

## 최초 helper 설치

신뢰된 오프라인 도구에서 `--bootstrap <game-root> <extracted-bundle-root> <independently-trusted-helper-SHA256>`를 사용합니다. expected SHA는 같은 downloaded bundle에서 가져오지 않고 승인된 독립 경로에서 얻습니다. public trust·서명·image SHA 검증 후 ancestor 경로와 read-only image handle을 고정한 채 외부 bundle helper를 실행합니다. 게임 root에서 직접 자기 자신을 설치하지 않습니다. 운영 trust가 없으면 실행 전 차단합니다.

실행되는 helper의 `--install-helper <root> <bundle>`는 게임·런처가 닫혔는지 확인하고 exact 8-file receipt와 실제 원본 hash를 확인합니다. helper를 8-file ownership에 넣지 않고 별도 receipt/journal로 관리합니다. 기존 unreceipted helper, 다른 image, legacy/shared-loader receipt, 없는 receipt는 수동 업그레이드 안내로 거부합니다. 다른 파일·공유 로더를 채택·교체하지 않습니다. 최초 설치 중단은 서명, root/work/file identity에 결합된 plan으로 `--recover-helper <root> <bap-helper-id>`가 finish-forward 하며 unknown 데이터는 덮어쓰지 않습니다. 완료 receipt와 committed plan에서 worker가 helper fingerprint를 확인합니다.

## 적용·복구·실행 gate

Apply는 서명된 incoming compatibility baseline으로 INT EXE, GameAssembly/UnityPlayer, 공유 loader/core, built-in 및 downloaded catalog inventory/hash를 다시 확인합니다. read-only identity leases를 첫 rename과 transaction 완료까지 유지합니다. 기존 정책대로 `data.unity3d`는 유효 UnityFS header가 필수이며 resource hash 차이만 advisory입니다. 호환성 전제 부재/변경은 첫 교체 전 수동 검토 상태이고 두 설정을 실패 OFF로 바꾸지 않습니다. 이미 transaction mutation 이후의 오류는 실패 latch와 rollback/forward recovery를 유지합니다.

복구 CLI는 signed ticket/descriptor를 재검증하고 같은 descriptor의 **pending journal만** 처리합니다. 과거 완료 journal과 backup은 건드리지 않아 재시도 B의 파일 ID를 과거 A에 잘못 대입하지 않습니다. 완료 후 재호출은 retained marker를 읽고 안전하게 완료를 반환합니다. live game/launcher/helper owner와 불명확·중복 pending 상태는 차단합니다.

모드 실행 PowerShell은 최신 launcher의 공통 recovery gate/lock을 게임 종료까지 보유합니다. Plugin.Load도 patches 전에 pending/corrupt evidence를 읽기 확인합니다. installer/uninstaller는 updater evidence가 있는 경우 같은 gate를 잡고 변경 전 차단하며 WhatIf는 읽기 검사만 합니다. 유효한 퇴역 failure와 완료 journal은 인게임 OFF·재활성화 UI를 이용하도록 허용합니다. 손상·pending·활성 guard 상태는 먼저 복구해야 합니다. vanilla 실행 경로/loader 기본 OFF는 변경하지 않았습니다.

## 완료 임시 출력 정리

`--clean-completed <root> -`는 root fault/operation/pending/corrupt transaction이 없고, ticket exclusive lease·완료 결과·서명 metadata·ZIP size/hash가 모두 확인된 **handoff stage 중복 package.zip 한 개**만 삭제합니다. transaction plan이 참조하는 stage, transaction work, rollback/new-slot backup, helper installation journal/receipt, 사용자 파일 및 서명/ticket/result 기록은 보존합니다. 정리 proof를 hash 검증 후 영속 기록하고 같은 file identity의 handle에 deletion-on-close를 설정합니다. 중단 후 반복 정리는 proof에 결합되어 idempotent입니다. 없는 ZIP에 proof를 새로 만들거나 변조·hardlink ZIP을 삭제하지 않습니다.

Windows deletion 구조와 제한은 [Microsoft FileDispositionInformation](https://learn.microsoft.com/en-us/openspecs/windows_protocols/ms-fscc/12c3dd1c-14f6-4229-9d29-75fb2cb392f6)에 맞춰 1-byte field 및 DELETE 권한을 사용합니다. recursive directory 삭제나 경로 문자열을 다시 따라가는 삭제는 없습니다.

보존된 work 128, pending ticket discovery stage 256, helper journal 64 및 retention scan 4096 한도를 넘으면 안전 차단합니다. 한도를 피하려고 journal/backup을 자동 삭제하지 않습니다. 장기 설치의 보존 공간·한도에 도달한 경우 승인된 수동 유지보수/복구 검토가 필요합니다. 이 제한은 자동 적용 가능성의 실질적인 한계입니다.

English: Offline prepare → external detached signatures → independently trusted finalize. First helper delivery is separate from eight-file ownership and requires a trusted outside-root bootstrap. Legacy/shared-loader ownership is manual only. Compatibility is rechecked before apply; pending/corrupt evidence blocks mod launch/maintenance. Cleanup deletes only authenticated completed duplicate handoff ZIPs and retains all recovery evidence/backups. Operational trust/private authentication, remote deployment, actual installation and native game validation remain unexecuted.
