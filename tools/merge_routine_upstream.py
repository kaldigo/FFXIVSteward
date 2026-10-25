"""Advance only to the exact validated merge, without force pushes or approvals."""
import argparse
import json
from pathlib import Path
import re
import subprocess


def output(*args):
    return subprocess.check_output(args, text=True).strip()


def merge(policy, default_branch, sync_branch, pr):
    if not policy['routine']:
        print('Manual review required. Pull request left open.')
        return False
    head, base = policy['head'], policy['base']
    if not all(re.fullmatch(r'[0-9a-f]{40}', sha) for sha in (head, base)):
        raise ValueError('Invalid recorded commit')
    if output('git', 'rev-parse', 'HEAD') != head:
        raise ValueError('Working commit changed after validation')
    for branch, expected in [(default_branch, base), (sync_branch, head)]:
        actual = output('git', 'ls-remote', '--exit-code', 'origin', f'refs/heads/{branch}').split()[0]
        if actual != expected:
            raise ValueError(f'{branch} changed while checks ran. Rerun upstream sync.')
    info = json.loads(output('gh', 'pr', 'view', pr, '--json',
                             'state,isDraft,headRefOid,baseRefOid,headRefName,baseRefName,author,isCrossRepository'))
    if (info['state'] != 'OPEN' or info['isDraft'] or info['isCrossRepository']
            or info['headRefOid'] != head or info['baseRefOid'] != base
            or info['headRefName'] != sync_branch or info['baseRefName'] != default_branch
            or info['author']['login'] != 'app/github-actions' or not info['author'].get('is_bot')):
        raise ValueError('Pull request identity or commit changed. Manual review required.')
    subprocess.run(['git', 'merge-base', '--is-ancestor', base, head], check=True)
    # A regular fast-forward push atomically rejects a concurrently advanced
    # default branch. GitHub marks the PR merged when its exact head lands.
    # No --force, --admin, review approval or branch-protection bypass is used.
    subprocess.run(['git', 'push', 'origin', f'{head}:refs/heads/{default_branch}'], check=True)
    print(f'Automatically merged the validated routine update: {pr}')
    return True


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--policy', required=True)
    parser.add_argument('--default-branch', required=True)
    parser.add_argument('--sync-branch', required=True)
    parser.add_argument('--pr', required=True)
    args = parser.parse_args()
    policy = json.loads(Path(args.policy).read_text(encoding='utf-8'))
    if merge(policy, args.default_branch, args.sync_branch, args.pr):
        from dispatch_routine_release import dispatch
        subprocess.run(['git', 'fetch', 'origin', args.default_branch], check=True)
        dispatch(policy, args.default_branch)
