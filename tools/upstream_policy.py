"""Fail-closed classification of the exact proposed upstream merge."""
import argparse
import json
from pathlib import PurePosixPath, Path
import re
import subprocess


def git(*args):
    return subprocess.check_output(['git', *args], text=True).strip()


def paths(before, after):
    return set(filter(None, subprocess.check_output(
        ['git', 'diff', '--name-only', '--no-renames', '-z', before, after]
    ).decode('utf-8').split('\0')))


def classify(base, upstream, head):
    base, upstream, head = [git('rev-parse', f'{ref}^{{commit}}') for ref in (base, upstream, head)]
    common = git('merge-base', base, upstream)
    fork_paths = paths(common, base)
    changed = paths(base, head) | paths(common, upstream)
    reasons = []
    expected = subprocess.run(['git', 'merge-tree', '--write-tree', base, upstream],
                              text=True, capture_output=True)
    if expected.returncode or expected.stdout.splitlines()[0] != git('rev-parse', f'{head}^{{tree}}'):
        reasons.append('Proposed tree differs from a clean merge of the recorded upstream and fork tips.')
    for path in sorted(changed):
        p = PurePosixPath(path)
        if path in fork_paths:
            reasons.append(f'{path}: modified by the fork.')
            continue
        # Dependencies, publishing, configuration and unrecognised files never
        # qualify. A narrow allow-list prevents new build files slipping through.
        routine_condition = False # Rotation, configuration and native-code changes always need review.
        routine_doc = (path == 'README.md' or path.startswith('docs/')) and p.suffix == '.md'
        if not (routine_condition or routine_doc):
            reasons.append(f'{path}: outside the routine documentation allow-list.')
            continue
        modes = [git('ls-tree', ref, '--', path).split(' ', 1)[0] for ref in (base, head)]
        if any(mode and mode != '100644' for mode in modes):
            reasons.append(f'{path}: special file mode, executable, symlink or submodule.')
        if not modes[1]:
            reasons.append(f'{path}: removals require manual review.')
        diff = git('diff', '--no-ext-diff', '--no-textconv', base, head, '--', path)
        if re.search(r'CloseAfterUse|OnActionInvoked|HotbarSlotNode|HotbarOverlayNode|HotbarConfig|TryCloseAfterUse|OnKeybindPressed|CustomActionManager|WrathFork|AutoRotation|SettingsTransaction|System\.Config|RaptureHotbarModule|PackageReference|ProjectReference|DllImport|LibraryImport|\bunsafe\b', diff):
            reasons.append(f'{path}: touches fork activation hooks, native interop or dependency declarations.')
    if not changed:
        reasons.append('No incoming changes to merge.')
    return {'base': base, 'upstream': upstream, 'head': head, 'routine': not reasons,
            'changed_paths': sorted(changed), 'reasons': reasons}


def report(policy):
    decision = 'Routine: eligible for automatic merge after all checks pass.' if policy['routine'] else 'Manual review required; automatic merge is disabled.'
    lines = [decision, '', 'Checks: build, configuration regressions, policy regressions and release-package validation.',
             'Routine automatic merges queue a validated, versioned release. Sensitive updates remain manual.', '', f"Validated base: `{policy['base']}`",
             f"Proposed head: `{policy['head']}`", '', 'Changed files:']
    lines.extend(f'- `{path}`' for path in policy['changed_paths'])
    if policy['reasons']:
        lines.extend(['', 'Reasons for manual review:'])
        lines.extend(f'- {reason}' for reason in policy['reasons'])
    if policy['routine']:
        receipt = {key: policy[key] for key in ['base', 'upstream', 'head']}
        lines.extend(['', '<!-- routine-release:' + json.dumps(receipt) + ' -->'])
    return '\n'.join(lines) + '\n'


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--base', required=True)
    parser.add_argument('--upstream', required=True)
    parser.add_argument('--head', default='HEAD')
    parser.add_argument('--output', required=True)
    args = parser.parse_args()
    policy = classify(args.base, args.upstream, args.head)
    Path(args.output).write_text(json.dumps(policy, indent=2) + '\n', encoding='utf-8')
    Path(args.output + '.md').write_text(report(policy), encoding='utf-8')
    print(report(policy))
