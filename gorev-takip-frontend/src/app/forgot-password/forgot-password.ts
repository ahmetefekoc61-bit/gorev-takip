import { ChangeDetectorRef, Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../services/auth';

@Component({
  selector: 'app-forgot-password',
  imports: [CommonModule, FormsModule, RouterLink],
  templateUrl: './forgot-password.html',
  styleUrl: './forgot-password.css'
})
export class ForgotPassword {
  email = '';
  loading = false;
  errorMessage = '';

  constructor(
    private authService: AuthService,
    private router: Router,
    private cdr: ChangeDetectorRef
  ) { }

  requestCode(): void {
    this.errorMessage = '';
    this.loading = true;

    this.authService.forgotPassword({ email: this.email }).subscribe({
      next: () => {
        this.loading = false;
        // Kod artık burada gösterilmiyor; e-postaya gönderildi. Kullanıcıyı kodu
        // gireceği ayrı sayfaya yönlendiriyoruz, e-postayı tekrar yazmasına gerek kalmasın diye taşıyoruz.
        this.router.navigate(['/reset-password'], { queryParams: { email: this.email } });
      },
      error: () => {
        this.loading = false;
        this.errorMessage = 'Bir hata oluştu, tekrar deneyin.';
        // Proje zoneless çalışıyor; async callback sonrası render'ı elle tetikliyoruz.
        this.cdr.markForCheck();
      }
    });
  }
}
