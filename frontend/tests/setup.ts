import '@testing-library/jest-dom';
import { beforeEach } from 'vitest';

beforeEach(() => {
  localStorage.setItem('wallet_seed_profile_id', 'Fernando');
});
