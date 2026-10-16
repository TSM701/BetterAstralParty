# GA 1.0.0 전체 후보와 승인·서명 계약

이 문서는 로컬 비공개 unsigned 후보 준비 계약이다. GA 출시, 실게임 검증, 설치·Steam 변경, GitHub 게시, 운영 key/token 사용 승인을 제공하지 않는다. source/build는 1.0.0/protocol1이며 실제 설치는 기존 RC.1 GS 진단본을 그대로 보존한다. README는 일반 모드 안내로 유지한다.

## 묶음과 검증

`tools/package-ga.ps1`은 명시적 input-manifest SHA-256을 받으며 원본 manifest, 모든 입력, 원본 upstream loader archive를 NTFS ancestor/file lease로 핀한다. 입력 schema/version/path/length/hash, 중복·별칭, unlisted/개발 파일, script parser, required/transitive installer closure, pinned Mono.Cecil, plugin identity/version/protocol, launcher exact safety API, helper entrypoint, GS/fixture marker와 private signing calls를 검사한다. input 최대 1,024개/256 MiB, 파일당 64 MiB, clean loader expanded 128 MiB로 제한한다. 기존 output, 입력/출력 겹침, hardlink/junction, 기존 native 경계의 절대경로 240자 초과는 거부한다. 실패한 부분 결과를 지우거나 덮어쓰지 않는다.

원본 .cache upstream BepInEx ZIP의 224개 승인 추출 항목을 비교하며 doorstop enabled=false만 명시적으로 변환한다. played-in loader tree를 배포 입력으로 쓰지 않는다. compatibility JSON은 기존 기준을 그대로 보존하고 loader hash를 대조한다. 현재 클라이언트/cache/실게임 호환성이 새로 검증됐다는 뜻은 아니다.

Whole 후보는 DLL/launcher/installer·uninstaller·bootstrap/log/Steam scripts/helper-bootstrap/manifest/catalog/hero/notices/clean loader를 함께 포함한다. owned8 update ZIP은 기존 receipt·transaction 계약의 8개 파일만 포함한다. helper와 catalog를 owned8에 추가하지 않는다. ZIP은 dot-directory를 포함하고 ordinal entry order와 고정 timestamp로 생성한다. legacy development archive의 기존 2인자 API는 유지하며 GA만 재현 가능한 3인자 API를 쓴다. 구 package-beta는 1.x GA 또는 확인된 obsolete GA DLL을 포장하지 않으며 명시적 pinned GA 경로로 안내한다. 구 development packager의 OFF/empty-trust 선언을 GA ON/private trial 후보에 재사용하지 않는다.

## identity와 catalog

설치 identity는 기존 `BetterAstralParty.InstallBundle/v1` 계약의 version/kind/protocol 및 ordinal path=SHA lines를 UTF-8/LF로 해시한다. `tools/approved-install-bundles.json`과 자체 manifest를 identity 입력에서 제외한다. 모든 실제 payload bytes, helper descriptor 및 **helper.signature가 존재하면 그 바이트까지** 포함한다. manifest는 schema/version/kind/buildIdentity와 모든 payload의 path/bytes/SHA를 포함하며 catalog bytes의 hash도 검증한다.

서명 전 후보는 empty catalog와 `catalog-proposal.json status=pending / exactApprovalReady=false`를 갖는다. installer는 이 후보를 `Unregistered installation bundle`로 거부해야 한다. script가 자신의 candidate를 승인하지 않는다. 현재 root production catalog도 빈 배열로 유지한다.

helper signature가 추가되면 identity는 바뀐다. **unsigned identity를 최종 승인으로 재사용하지 않는다.** 서명 추가 후 same fixed products/scripts로 empty-catalog 묶음을 새로 생성해 그 identity와 `review-inventory.json` SHA를 독립 검토한다. 외부 승인 record의 purpose/status/version/kind/protocol/buildIdentity/reviewInventorySha256/reviewReference와 별도의 record SHA가 정확히 일치하고 helper signature가 production public pin으로 검증될 때에만 `-ApprovalRecord / -ApprovalSha256` 경로가 새 payload catalog에 하나의 exact tuple을 등록한다. 모든 승인 scalar 타입을 확인한다. 이 경로는 이번 unsigned 준비에서 정상 승인·등록을 실행하지 않았다.

등록된 catalog는 trust된 배포 입력의 검토 기록이다. 암호학적 다운로드 인증이나 private 접근 권한이 아니다. catalog 등록은 content identity를 유지하지만 manifest/catalog/whole ZIP bytes 및 hash를 바꾼다. exact final bytes를 다시 고정해야 한다.

## helper 서명과 업데이트 descriptor

`GaPackageContract.PrepareHelper`는 실제 `HelperDescriptor.Prepare`의 canonical descriptor를 생성한다. ASCII/LF/final LF, repository/version/platform/PE x64 image/bytes/SHA 계약을 공유한다. current unsigned helper는 `helper-bootstrap/BetterAstralParty-UpdateHelper.exe`에 있으며 운영 signature는 없다. 기존 signature를 새 helper에 붙이거나 key를 읽지 않는다. signature 입력은 후보의 descriptor 원문과 SHA-256이다. `signing-inputs/helper-signing-input.json`에 정확한 bytes/hash/public pin/algorithm/envelope 절차를 둔다. 운영 key의 GA 사용 및 signing은 owner의 별도 단계다. public pin/key ID와 memory-only credential input gate는 기존 trial 구현 그대로며 credential 연결 여부를 확인하지 않았다.

서명 envelope는 `BetterAstralParty.UpdateSignature/v1`, `key-id=<compiled approved public key ID>`, `signature=<base64 RSA-PSS-SHA256 signature>`, 마지막 빈 LF 줄이다. owner가 정확한 descriptor bytes를 승인된 offline signer에서 처리한다. source 또는 binary를 다시 빌드하거나 descriptor 내용을 바꾸면 서명/identity/승인이 모두 달라진다. trial public pin을 GA 운영 key 승인으로 간주하지 않는다. key 교체는 별도 public-pin source 변경·3개 product 재빌드·전체 재검토가 필요하다.

업데이트 payload ZIP의 SHA는 이미 확정할 수 있지만 signed update descriptor는 실제 private GitHub release ID와 update ZIP asset ID에 바인딩된다. 아직 ID가 없으므로 운영 descriptor를 만들거나 임의 ID를 넣지 않는다. `tools/prepare-ga-update-descriptor.ps1`은 이후 허용된 단계에서 실제 positive IDs와 정확한 candidate-state SHA를 받아 ZIP owned8의 순서/경로/중복/길이/hash를 다시 확인한 후 unsigned descriptor 원문을 생성한다. 이 tool도 서명·자격증명·네트워크 작업을 하지 않는다. fixture ID 123/456은 descriptor 테스트 폴더에만 존재하며 운영 signing deliverable에 포함하지 않는다.

첫 helper 전달은 signed offline bootstrap의 별도 workflow다. 기존 unreceipted/다른 helper를 자동 교체하지 않는다. 기존 helper 상태의 upgrade/recovery는 별도 검토 대상이며 이번에 실제 --install-helper/--apply/--recover를 실행하지 않았다.

## 성공·실패·미실행

stage `artifacts/automatic-update-development/ga-whole-candidate-20261003-b1`이 고정 검토 대상이다. plugin 75 source/16 pinned refs, full launcher/helper Framework closures를 격리 빌드했다. 세 product의 marker/type 검사가 통과했다. actual GA launcher의 304개 nested methods/fields/IL/locals/EH가 독립 검토된 b2와 일치하며 전체 파일 bit hash가 같다고 주장하지 않는다.

packager 첫 실행은 13/20 예상 결과였다. 7개 full-input fixture가 existing 240자 정책을 넘어서 의도 guard에 도달하지 못했다. 짧은 새 fixture에서 그 7개와 hardlink/junction 2개를 9/9 확인했다. 집계는 **distinct 22개 예상 결과**이며 단일 22/22 실행이라고 서술하지 않는다. 실패한 경로와 로그를 보존했다. 두 whole ZIP/owned8 ZIP의 각 entry bytes/hash 및 재현 가능한 ZIP bytes를 확인했다.

PS5.1/7 extracted package 255 entry/hash/parser/error-log/missing-bootstrap CMD 검사가 통과했다. 두 host의 실제 GA ReadInstallerState 읽기 8개씩 및 whole-candidate empty catalog 거부 검사도 통과했다. descriptor fixture 4개는 canonical/zero ID/state hash/duplicate ZIP을 확인했다. actual signing, valid signed approval finalization, key rotation, live game/native UI/General crash/실제 Steam/운영 private HTTP/auth/cache/Stable-Beta/offline와 모든 installer failure·power-loss 경계는 검증하지 않았다. 설치·helper receipt·pending journal·백업·다른 모드와 source b1/b2 고정자료를 보존한다.

다음 단계는 이 고정 unsigned 후보와 package source의 독립 검토, owner signing/key 사용 판단, signed-helper identity의 별도 catalog 승인, 실제 private 대상/ID/서명된 update descriptor 조율, 이후 별도 허용된 설치·실게임 검증이다.

